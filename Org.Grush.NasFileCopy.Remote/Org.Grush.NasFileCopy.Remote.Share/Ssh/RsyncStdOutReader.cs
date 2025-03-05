using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share.Ssh;

/// <summary>
///
/// </summary>
/// <remarks>
/// <b>Re: PIDs:</b>
/// At least on macOS, the PID of the shell execution and the STDOUT-%p are the same.
/// BUT they differ from the [boxed] log PID and the logger-%p.
///
/// An example `ps aux` output found:
/// shell-backgrounded PID and stdout-%p: 68221
/// log [boxed] PID:                      68225
/// logged-%p:                            68226     = ps-aux: a seemingly-identical-to-68221 cmd
/// </remarks>
public sealed class RsyncStdOutReader : IAsyncDisposable
{
  private const string SEP = " /// ";
  /// <summary>
  /// <a href="https://linux.die.net/man/5/rsyncd.conf#:~:text=The%20single%2Dcharacter%20escapes%20that%20are%20understood%20are%20as%20follows">See man rsyncd.conf(5)</a>
  /// </summary>
  /// <example>
  /// 2025/03/03 12:11:31 [67654]  /// 67655 /// 1628522496 /// >f+++++++ /// Users/samuel/dest/TrueNAS-SCALE-24.04.1.1 copy 2.iso
  /// </example>
  private const string LogFmt = $"{SEP}%i{SEP}%b{SEP}%p{SEP}%f{SEP}";
  private const string StdArgs = $"--verbose --archive --no-o --no-g --times  --log-file-format=\"{LogFmt}\"  --stats -P";

  private static readonly Regex LogPrefixRe = new(@"^(?<datetime>[\d/]{10} [\d:]{8}) \[(?<boxed-pid>\d+)\] ");
  private static readonly Regex LogRe = new(@"^ /// (?<pid>\d+) /// (?<fileBytes>\d+) /// (?<itemizedChanges>\S+) /// (?<fileName>.*)$");

  private readonly SshClient _client;
  private readonly CancellationTokenSource _cancellationTokenSource = new();
  private readonly string _rsyncCommandText;

  // private SshCommand? _sshCommand;
  private uint? ProcessId { get; set; }

  public string RunLabel { get; }
  public string LogFilePath { get; }
  public RsyncState? State { get; private set; }
  public ImmutableDictionary<string, (string Value, string? Unit)>? FinalStats { get; private set; }

  public TimeSpan ListenDelay { get; set; } = TimeSpan.FromSeconds(1);
  public uint EmptyListenDelaysBeforeRecheck { get; set; } = 5;

  public RsyncStdOutReader(
    SshClient client,
    string copyFrom,
    string destination,
    CancellationToken cancellationToken
  )
  {
    Debug.Assert(copyFrom[0] is '\'');
    Debug.Assert(destination[0] is '"');

    _client = client;

    RunLabel = DateTime.Now.ToString("yyyyMMddHHmmss");
    LogFilePath = $"\"$HOME/rsync.{RunLabel}.log\"";

    _rsyncCommandText = $"sudo rsync {StdArgs} --log-file={LogFilePath} {copyFrom} {destination}";

    cancellationToken.Register(_cancellationTokenSource.Cancel);
  }

  private static readonly Regex LastLineNotStartingWihSpaceRe = new(
    @"^(?<line>\S[^\n]*)\n",
    RegexOptions.Multiline | RegexOptions.RightToLeft
  );
  // private readonly Regex LastLineRe = new(
  //   @"^[^\n]*\n",
  //   RegexOptions.Multiline | RegexOptions.RightToLeft
  // );

  private static readonly Regex SpeedRe = new(@"^ *(?<num>[\d\.]+)(?<unit>[kMB])b/s");
  private static readonly Regex RsyncProgressRe = new(@"""
    ^\ +(?<bytes>\d+)\ +(?<percent>\d{1,3})\%\ +(?<rate>[\d\.]+[kMB]?b/s)\ +(?<time>[\d\:\?])\ *(\(xfer\#(?<xfer>\d+),\ to-check=(?<tocheck>[\d/]+))?\n
    """, RegexOptions.Multiline);

  private static readonly Regex FinalStatLineRe = new(@"^(?<key>[^:]): *(?<value>\S+)( +(?<units>.*))?$");

  private void ReadFinalStats(string finalState)
  {
    FinalStats = finalState.Split('\n')
      .Select(line => FinalStatLineRe.Match(line))
      .Where(match => match.Success)
      .ToImmutableDictionary(
        match => match.Groups["key"].Value,
        match => (match.Groups["value"].Value, Unit: match.Groups["units"].Success ? match.Groups["units"].Value : null)
      );
  }

  private static readonly string IdFinderOutput = "<${?}=${!}>";
  private static readonly Regex IdFinderRe = new(@"<(?<exit>\d+)=(?<pid>\d+)>");
  public async Task StartNewAsync()
  {
    if (ProcessId.HasValue)
      throw new InvalidOperationException("Re-execution");

    var cmd = _client.RunCommand($"{_rsyncCommandText} & ; echo \"{IdFinderOutput}\"");

    var result = await Task.Factory.FromAsync(
      asyncResult: cmd.BeginExecute(),
      endMethod: cmd.EndExecute
    );

    var match = IdFinderRe.Match(result);
    byte exitCode = byte.Parse(match.Groups["exit"].Value);
    uint pid = uint.Parse(match.Groups["pid"].Value);

    if (exitCode is not 0)
      throw new RsyncCommandFailedException(exitCode, $"{nameof(StartNewAsync)} failed");
    if (pid is 0)
      throw new RsyncCommandFailedException(0, $"{nameof(StartNewAsync)} returned 0 but pid is 0.");
    ProcessId = pid;
  }

  public class RsyncException(string message) : Exception(message);
  public class RsyncProcessNotFoundException(string message) : RsyncException(message);

  public class RsyncCommandFailedException(byte exitCode, string? message)
    : RsyncException($"rsync exited with {exitCode}{(message is null ? "" : $"; {message}")}");

  private async Task<uint> GetProcessDirOrThrow(TrueNasSshClient client)
  {
    if (ProcessId is null)
      throw new Exception($"First {nameof(StartNewAsync)}");

    string procDir = $"/proc/{ProcessId.Value}/";

    var lsDir = await client.LsVerboseAsync(procDir, _cancellationTokenSource.Token);

    if (!lsDir.Success)
      throw new RsyncProcessNotFoundException($"ls on /proc/ dir returned {lsDir.ExitStatus} {lsDir.Commentary}");

    return ProcessId.Value;
  }

  public async IAsyncEnumerable<RsyncState> ListenAsync(TrueNasSshClient client)
  {
    var procId = await GetProcessDirOrThrow(client);
    using var tailCommand = _client.RunCommand($"tail --pid {procId} -f /proc/{procId}/fd/1");

    const int capacity = 4 * 1024;
    var token = _cancellationTokenSource.Token;
    token.ThrowIfCancellationRequested();

    string currentState = "";
    int iterationsSinceNoOutput = 0;

    await using var bufferedStream = new BufferedStream(tailCommand.OutputStream, capacity);
    while (true) // breaks if reaches the end OR in the delay at the end
    {
      var bytes = new byte[capacity];

      var bytesRead = await bufferedStream.ReadAtLeastAsync(bytes, capacity, throwOnEndOfStream: false, cancellationToken: token);

      if (bytesRead < 1)
      {
        ++iterationsSinceNoOutput;
        if (iterationsSinceNoOutput > EmptyListenDelaysBeforeRecheck)
        {
          await GetProcessDirOrThrow(client); // ignore output, check that process is still connected
          iterationsSinceNoOutput = 0;
        }
      }
      else
      {
        iterationsSinceNoOutput = 0;
        if (bytesRead == capacity) // if we ReadAtLeast an entire capacity full, we have left our old currentState behind
          currentState = "";

        currentState += Encoding.UTF8.GetString(bytes, 0, bytesRead);

        if (currentState.Contains("\n\n"))
        {
          ReadFinalStats(currentState);
          yield break;
        }
        // else assume we're not at the end.... TODO: is that a safe assumption?

        if (RsyncProgressRe.Match(currentState) is { Success: true } lastProgressLine)
        {
          var remaining = lastProgressLine.Groups["time"].Value;
          ulong? xfer = lastProgressLine.Groups["xfer"].Success ? ulong.Parse(lastProgressLine.Groups["xfer"].Value) : null;
          (ulong, ulong)? toCheck = lastProgressLine.Groups["tocheck"] is { Success: true } succ && succ.Value.Split('/') is {} parts
            ? (ulong.Parse(parts[0]), ulong.Parse(parts[1]))
            : null;

          string? latestFile = null;

          if (LastLineNotStartingWihSpaceRe.Match(currentState, lastProgressLine.Index) is
              { Success: true } lastFileLine)
          {
            latestFile = lastFileLine.Groups["line"].Value;
          }

          State = new(
            LatestFile: latestFile ?? State?.LatestFile ?? "??",
            LatestFileByteProgress: ulong.Parse(lastProgressLine.Groups["bytes"].Value),
            LatestFilePercent: byte.Parse(lastProgressLine.Groups["percent"].Value),
            LatestFileSpeed: ReadSpeedToBytes(lastProgressLine.Groups["rate"].Value),
            LatestFileRemainingTime: remaining.Contains('?') ? TimeSpan.MaxValue : TimeSpan.Parse(remaining),
            TotalXferCount:  xfer ?? State?.TotalXferCount ?? 0,
            ToCheckNumerator: toCheck?.Item1 ?? State?.ToCheckNumerator ?? 0,
            ToCheckDenominator: toCheck?.Item2 ?? State?.ToCheckDenominator ?? 0
          );

          int idxOfNextCharAfterEndOfProgressLine = lastProgressLine.Index + lastProgressLine.Length;
          int distanceFromEndOfStream = currentState.Length - idxOfNextCharAfterEndOfProgressLine;

          bufferedStream.Seek(-distanceFromEndOfStream, SeekOrigin.End);

          yield return State;
        }
        else
        {
          // didn't find an rsync progress line
        }
      }

      await Task.Delay(ListenDelay, token);
    }
  }

  private ulong ReadSpeedToBytes(string speed)
  {
    if (SpeedRe.Match(speed) is not { Success: true } floatMatch)
      return 0;

    // https://github.com/RsyncProject/rsync/blob/9994933c8ccf7ead27c81fe4ce2eb4e08af20c7f/progress.c#L108-L116
    var pow = floatMatch.Groups["unit"].Value switch
    {
      "k" => 1,
      "M" => 2,
      "G" => 3,
      _ => 0,
    };

    var mult = Math.Pow(1024, pow);

    return (ulong)(float.Parse(floatMatch.Groups["num"].Value) * mult);
  }

  public async ValueTask DisposeAsync()
  {
    _cancellationTokenSource.Dispose();
  }
}