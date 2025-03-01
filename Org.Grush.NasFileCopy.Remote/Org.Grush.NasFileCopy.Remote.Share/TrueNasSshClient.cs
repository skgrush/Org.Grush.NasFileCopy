using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share;

public class DangerousOperationException(string Message) : InvalidOperationException(Message);

public record SshResult(
  bool Success,
  bool CommandFinished,
  bool? Result = null,
  byte? ExitStatus = null,
  string? Output = null,
  string? Error = null,
  Exception? Exception = null
) : SshResult<bool>(Success, CommandFinished, Result, ExitStatus, Output, Error, Exception)
{
  public new static SshResult Threw(Exception e)
    => new(Success: false, CommandFinished: false, Exception: e);

  public static SshResult Completed(bool success, SshCommand cmd)
    => new(
      Success: success,
      CommandFinished: true,
      Result: success,
      ExitStatus: (byte)cmd.ExitStatus,
      Output: cmd.Result,
      Error: cmd.Error
    );
}

public record SshResult<T>(
  [property: MemberNotNullWhen(true, nameof(Result))]
  bool Success,
  [property: MemberNotNullWhen(true, nameof(ExitStatus))]
  [property: MemberNotNullWhen(true, nameof(Output))]
  [property: MemberNotNullWhen(true, nameof(Error))]
  [property: MemberNotNullWhen(false, nameof(Exception))]
  bool CommandFinished,
  T? Result = null,
  byte? ExitStatus = null,
  string? Output = null,
  string? Error = null,
  Exception? Exception = null
) where T : struct
{
  public static SshResult<T> Threw(Exception e)
    => new(Success: false, CommandFinished: false, Exception: e);

  public static SshResult<T> Completed(bool success, T result, SshCommand cmd)
    => new(
      Success: success,
      CommandFinished: true,
      Result: result,
      ExitStatus: (byte)cmd.ExitStatus,
      Output: cmd.Result,
      Error: cmd.Error
    );

  public static implicit operator SshResult<T>((T, SshCommand) pair)
      => Completed(true, pair.Item1, pair.Item2);
}


public sealed class TrueNasSshClient(
  ConnectionInfo sshCredentials
) : IAsyncDisposable
{
  private readonly SshClient _sshClient = new(sshCredentials);
  private readonly Regex _dangerousShellQuoteChars = new("\\n|\"");

  public async Task ConnectAsync(CancellationToken cancellationToken)
  {
    await _sshClient.ConnectAsync(cancellationToken);
  }

  public async Task<SshResult> MountAsync(string devPath, string destinationPath, CancellationToken cancellationToken)
  {
    try
    {
      if (devPath.Contains('\''))
        throw new DangerousOperationException("devPath cannot contain '");
      if (_dangerousShellQuoteChars.IsMatch(destinationPath))
        throw new DangerousOperationException("destinationPath contains dangerous characters.");
      using var command = _sshClient.RunCommand($"mount -rw '{devPath}' \"{destinationPath}\"");

      var result = await Task.Factory.FromAsync(
        asyncResult: command.BeginExecute(),
        endMethod: command.EndExecute
      );

      return SshResult.Completed(command.ExitStatus is 0, command);
    } catch (Exception e)
    {
      return SshResult.Threw(e);
    }
  }

  public async Task<SshResult<ImmutableArray<string>>> LsAsync(string path, CancellationToken cancellationToken)
  {
    try
    {
      if (_dangerousShellQuoteChars.IsMatch(path))
        throw new DangerousOperationException("path contains dangerous characters.");
      path = path.Replace("~", "$HOME");

      using var command = _sshClient.RunCommand($"ls -1 \"{path}\" ");

      await Task.Factory.FromAsync(
        asyncResult: command.BeginExecute(),
        endMethod: command.EndExecute
      );

      if (command.ExitStatus is not 0)
        throw new InvalidOperationException($"ls shouldn't throw? It exited {command.ExitStatus}");

      return (
        [
          ..command.Result
            .TrimEnd('\n')
            .Split('\n')
        ],
        command
      );
    }
    catch (Exception e)
    {
      return SshResult<ImmutableArray<string>>.Threw(e);
    }
  }

  public void RsyncSession(
    string copyFrom,
    string destination
  )
  {
    if (copyFrom.Contains('\''))
      throw new DangerousOperationException("copyFrom cannot contain '");
    if (_dangerousShellQuoteChars.IsMatch(destination))
      throw new DangerousOperationException("destination contains dangerous characters.");

    var command = _sshClient.RunCommand($"sudo rsync --verbose --archive --no-o --no-g --stats --info=progress2 --info=name0 '{copyFrom}' \"{destination}\"");

  }

  public RsyncReader Rsync(
    string copyFrom,
    string destination,
    CancellationToken cancellationToken
  )
  {
    if (copyFrom.Contains('\''))
      throw new DangerousOperationException("copyFrom cannot contain '");
    if (_dangerousShellQuoteChars.IsMatch(destination))
      throw new DangerousOperationException("destination contains dangerous characters.");

    return new RsyncReader(
      _sshClient,
      $"sudo rsync --verbose --archive --no-o --no-g --stats --info=progress2 --info=name0 '{copyFrom}' \"{destination}\"",
      cancellationToken: cancellationToken
    );
  }

  public async ValueTask DisposeAsync()
  {
    _sshClient.Dispose();
  }
}

public record RsyncState(
  string LatestFile,

  ulong LatestFileByteProgress,
  byte LatestFilePercent,
  float LatestFileSpeed,
  TimeSpan LatestFileRemainingTime,

  ulong TotalXferCount,
  ulong ToCheckNumerator,
  ulong ToCheckDenominator
);

public sealed class RsyncReader : IAsyncDisposable
{
  private readonly SshCommand _sshCommand;
  private readonly CancellationTokenSource _cancellationTokenSource = new();

  public RsyncState? State { get; private set; }
  public ImmutableDictionary<string, (string Value, string? Unit)>? FinalStats { get; private set; }

  public TimeSpan ListenDelay { get; set; } = TimeSpan.FromSeconds(1);

  public RsyncReader(SshClient client, string cmd, CancellationToken cancellationToken)
  {
    _sshCommand = client.RunCommand(cmd);

    _cancellationTokenSource.Token.Register(_sshCommand.CancelAsync);
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

  public async IAsyncEnumerable<RsyncState> Listen()
  {
    const int capacity = 4 * 1024;
    var token = _cancellationTokenSource.Token;
    token.ThrowIfCancellationRequested();

    string currentState = "";

    await using var bufferedStream = new BufferedStream(_sshCommand.OutputStream, capacity);
    while (true) // breaks if reaches the end OR in the delay at the end
    {
      var bytes = new byte[capacity];

      var bytesRead = await bufferedStream.ReadAtLeastAsync(bytes, capacity, throwOnEndOfStream: false, cancellationToken: token);

      if (bytesRead > 0)
      {
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
    _sshCommand.Dispose();
    _cancellationTokenSource.Dispose();
  }
}