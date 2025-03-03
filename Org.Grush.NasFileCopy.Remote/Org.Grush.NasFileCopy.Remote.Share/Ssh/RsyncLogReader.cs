using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share.Ssh;

public record RsyncLogEntry(
  DateTime Timestamp,
  uint LoggerPid,
  uint RsyncPid,
  ulong BytesTransferred,
  string ItemizedChanges,
  string Filename
);

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
public sealed class RsyncLogReader : IAsyncDisposable
{
  private const string RsyncLogDir = "$HOME/Org.Grush.NasFileCopy/rsync-logs";
  private const string SEP = " /// ";
  /// <summary>
  /// <a href="https://linux.die.net/man/5/rsyncd.conf#:~:text=The%20single%2Dcharacter%20escapes%20that%20are%20understood%20are%20as%20follows">See man rsyncd.conf(5)</a>
  /// </summary>
  /// <example>
  /// 2025/03/03 12:11:31 [67654]  /// 67655 /// 1628522496 /// >f+++++++ /// Users/samuel/dest/TrueNAS-SCALE-24.04.1.1 copy 2.iso ///
  /// </example>
  private const string LogFmt = $"{SEP}%p{SEP}%b{SEP}%i{SEP}%f{SEP}";
  private const string StdArgs = $"--verbose --archive --no-o --no-g --times  --log-file-format=\"{LogFmt}\"  --stats -P";

  private static readonly Regex LogPrefixRe = new(@"^(?<datetime>[\d/]{10} [\d:]{8}) \[(?<boxed-pid>\d+)\] (?<contents>[^\n]*)$", RegexOptions.Multiline);
  private static readonly Regex LogRe = new(@"^ /// (?<pid>\d+) /// (?<fileBytes>\d+) /// (?<itemizedChanges>\S+) /// (?<fileName>.*) /// $");
  private static readonly Regex FinalLogRe = new(@"^sent\s+(?<sent>\d+)\s+bytes\s+received(?<recv>\d+)\s+bytes\s+total size\s+(?<total>\d+)$");

  private readonly SshClient _client;
  private readonly CancellationTokenSource _cancellationTokenSource = new();
  private readonly string _rsyncCommandText;

  private uint? ProcessId { get; set; }

  public string RunId { get; }
  public string LogFilePath { get; }
  public string LockFileBaseName { get; }
  public State? CurrentState { get; private set; }
  // public ImmutableDictionary<string, (string Value, string? Unit)>? FinalStats { get; private set; }

  public TimeSpan ListenDelay { get; set; } = TimeSpan.FromSeconds(1);
  public uint EmptyListenDelaysBeforeRecheck { get; set; } = 5;

  public RsyncLogReader(
    SshClient client,
    string copyFrom,
    string destination,
    CancellationToken cancellationToken
  )
  {
    Debug.Assert(copyFrom[0] is '\'');
    Debug.Assert(destination[0] is '"');

    _client = client;

    RunId = DateTime.Now.ToString("yyyyMMddHHmmss");
    LogFilePath = $"{RsyncLogDir}/rsync.{RunId}.log";
    LockFileBaseName = $"rsync.{RunId}.lock";

    _rsyncCommandText = $"sudo -n rsync {StdArgs} --log-file=\"{LogFilePath}\" {copyFrom} {destination}";

    cancellationToken.Register(_cancellationTokenSource.Cancel);
  }

  private const string IdFinderOutput = "<${?}=${!}>";
  private static readonly Regex IdFinderRe = new(@"<(?<exit>\d+)=(?<pid>\d+)>");
  public async Task StartNewAsync(string password)
  {
    if (ProcessId.HasValue)
      throw new InvalidOperationException("Re-execution");

    var token = _cancellationTokenSource.Token;

    using (var mkdirLogCmd = _client.CreateCommand($"mkdir -p {RsyncLogDir}"))
    {
      await mkdirLogCmd.ExecuteAsync(token);
      if (mkdirLogCmd.ExitStatus is not 0)
        throw new RsyncException($"Failed to create {RsyncLogDir}");
    }

    using (var sudoCmd = _client.CreateCommand("sudo -S echo 'X'"))
    {
      await using (var sudoInput = sudoCmd.CreateInputStream())
      {
        await sudoInput.WriteAsync(Encoding.UTF8.GetBytes(password + '\n'), cancellationToken: token);
      }

      await sudoCmd.ExecuteAsync(cancellationToken: token);
      if (sudoCmd.ExitStatus is not 0)
        throw new RsyncException("Password or permission refused");
    }

    using var cmd = _client.RunCommand($"{_rsyncCommandText} & ; echo \"{IdFinderOutput}\"");

    await cmd.ExecuteAsync(cancellationToken: token);

    var match = IdFinderRe.Match(cmd.Result);
    byte exitCode = byte.Parse(match.Groups["exit"].Value);
    uint pid = uint.Parse(match.Groups["pid"].Value);

    if (exitCode is not 0)
      throw new RsyncCommandFailedException(exitCode, $"{nameof(StartNewAsync)} failed with output: {cmd.Result}|{cmd.Error}");
    if (pid is 0)
      throw new RsyncCommandFailedException(0, $"{nameof(StartNewAsync)} returned 0 but pid is 0.");
    ProcessId = pid;

    await CreateLockFileAsync();
  }



  public async IAsyncEnumerable<State> ListenAsync(TrueNasSshClient client)
  {
    var processIdFromLockFile = await ReadIdFromLockFileAsync();

    if (ProcessId != processIdFromLockFile)
      throw new RsyncException($"Mismatched pids: {ProcessId}|{processIdFromLockFile}");

    var token = _cancellationTokenSource.Token;
    token.ThrowIfCancellationRequested();

    using var tailCommand = _client.CreateCommand($"tail --pid {ProcessId} -f \"{LogFilePath}\"");
    var executeTask = tailCommand.ExecuteAsync(token);

    const int capacity = 4 * 1024;

    string currentState = "";
    int iterationsSinceNoOutput = 0;

    await using var bufferedStream = new BufferedStream(tailCommand.OutputStream, capacity);
    while (true) // breaks if reaches the end OR in the delay at the end
    {

      // read into the string until we hit the end
      int bytesRead = 0;
      int bytesReadThisLoop;
      do
      {
        var bytes = new byte[capacity];
        bytesReadThisLoop = await bufferedStream.ReadAsync(bytes, 0, capacity, token);
        bytesRead += bytesReadThisLoop;

        if (bytesReadThisLoop > 0)
          currentState += Encoding.UTF8.GetString(bytes, 0, bytesReadThisLoop);

      } while (bytesReadThisLoop > 0);

      if (bytesRead < 1)
      {
        ++iterationsSinceNoOutput;
        if (iterationsSinceNoOutput > EmptyListenDelaysBeforeRecheck)
        {
          if (tailCommand.ExitStatus is not null || executeTask.IsCompleted)
            yield break;

          iterationsSinceNoOutput = 0;
        }
      }
      else
      {
        iterationsSinceNoOutput = 0;
        if (bytesRead == capacity)  // if we ReadAtLeast an entire capacity full, we have left our old currentState behind
          currentState = "";

        int lastIdx = 0;
        foreach (Match lineMatch in LogPrefixRe.Matches(currentState))
        {
          var lineContents = lineMatch.Groups["contents"].Value;

          if (LogRe.Match(lineContents) is { Success: true } logMatch)
          {
            var fileBytes = ulong.Parse(logMatch.Groups["fileBytes"].Value);
            var total = (CurrentState?.TotalBytes ?? 0) + fileBytes;
            State state = new(
              CurrentFile: logMatch.Groups["fileName"].Value,
              TotalBytes: total,
              CurrentFileBytes: fileBytes
            );
            CurrentState = state;
            yield return state;
          }
          else if (FinalLogRe.Match(lineContents) is { Success: true } finalLogMatch)
          {
            State state = new(
              CurrentFile: "",
              CurrentFileBytes: CurrentState?.TotalBytes ?? 0,
              TotalBytes: ulong.Parse(finalLogMatch.Groups["total"].Value)
            );
            CurrentState = state;
            yield return state;
          }
          // else ignored line

          lastIdx = lineMatch.Index + lineMatch.Length;
        }
        // read all of the current tream

        int distanceFromEndOfStream = currentState.Length - lastIdx;

        bufferedStream.Seek(-distanceFromEndOfStream, SeekOrigin.End);
      }

      await Task.Delay(ListenDelay, token);
    }
  }

  private async Task<uint> ReadIdFromLockFileAsync()
  {
    var quotedLockFilePath = $"\"{RsyncLogDir}/{LockFileBaseName}\"";
    var token = _cancellationTokenSource.Token;

    using var readLockCmd = _client.CreateCommand($"cat {quotedLockFilePath}");
    await readLockCmd.ExecuteAsync(token);

    if (readLockCmd.ExitStatus is not 0)
      throw new RsyncException($"Failed to {nameof(ReadIdFromLockFileAsync)} for {RunId}");

    return uint.Parse(readLockCmd.Result.Trim());
  }

  private async Task CreateLockFileAsync()
  {
    if (!ProcessId.HasValue)
      throw new ArgumentException($"Cannot {nameof(CreateLockFileAsync)} before started.");

    var quotedLockFilePath = $"\"{RsyncLogDir}/{LockFileBaseName}\"";
    var token = _cancellationTokenSource.Token;

    using (var checkFileExistsCmd = _client.CreateCommand($"[ -f {quotedLockFilePath} ]"))
    {
      await checkFileExistsCmd.ExecuteAsync(token);

      if (checkFileExistsCmd.ExitStatus is 0)
        throw new RsyncException(
          $"Lock file {quotedLockFilePath} already exists in {nameof(RsyncLogDir)} while attempting to {nameof(CreateLockFileAsync)}");
    }

    using var createCmd = _client.CreateCommand($"echo {ProcessId.Value} >{quotedLockFilePath}");
    await createCmd.ExecuteAsync(cancellationToken: token);

    if (createCmd.ExitStatus is not 0)
      throw new RsyncException($"Failed to {nameof(CreateLockFileAsync)}; exit status {createCmd.ExitStatus}");
  }

  private async Task DeleteLockFileAsync()
  {
    using var rmCmd = _client.CreateCommand($"rm \"{RsyncLogDir}/{LockFileBaseName}\"");
    await rmCmd.ExecuteAsync(cancellationToken: CancellationToken.None);

    if (rmCmd.ExitStatus is not 0)
      throw new RsyncException($"Failed to {nameof(DeleteLockFileAsync)}; exit status {rmCmd.ExitStatus}");
  }

  private async Task<string[]> ReadCmdlineForProcess(TrueNasSshClient client)
  {
    if (!ProcessId.HasValue)
      throw new RsyncException($"Attempt to {nameof(ReadCmdlineForProcess)} before {nameof(StartNewAsync)}");

    var cmdLineResult = await client.ReadFileAsync($"/proc/{ProcessId}/cmdline", cancellationToken: _cancellationTokenSource.Token);
    if (!cmdLineResult.Success)
      throw new RsyncProcessNotFoundException($"Proc folder for {ProcessId} cmdline not found");

    return cmdLineResult.Result.Value.contents.Trim('\n').Split('\0');
  }

  private async Task<ImmutableDictionary<string, string>> ReadStatsForProcess(TrueNasSshClient client)
  {
    if (!ProcessId.HasValue)
      throw new RsyncException("ProcessId not yet");

    var statusResult = await client.ReadFileAsync($"/proc/{ProcessId}/status", cancellationToken: _cancellationTokenSource.Token);
    if (!statusResult.Success)
      throw new RsyncProcessNotFoundException($"Proc folder for {ProcessId} status not found");

    return statusResult.Result.Value
        .contents
        .Split('\n')
        .Select(line => line.Split(":\t", 2))
        .Where(pair => pair.Length is 2)
        .ToImmutableDictionary(pair => pair[0], pair => pair[1]);
  }


  public class RsyncException(string message) : Exception(message);
  public class RsyncProcessNotFoundException(string message) : RsyncException(message);

  public class RsyncCommandFailedException(byte exitCode, string? message)
    : RsyncException($"rsync exited with {exitCode}{(message is null ? "" : $"; {message}")}");

  public record struct State(
    string CurrentFile,
    ulong CurrentFileBytes,
    ulong TotalBytes
  );

  ValueTask IAsyncDisposable.DisposeAsync()
  {
    _cancellationTokenSource.Cancel();
    _cancellationTokenSource.Dispose();

    return default;
  }
}