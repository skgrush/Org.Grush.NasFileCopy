using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Org.Grush.NasFileCopy.Remote.Share.Ssh;

/// <summary>
/// Read from an rsync log file as it streams.
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
internal sealed class RsyncLogReader : IAsyncDisposable
{
  private const string RsyncLogDir = "$HOME/Org.Grush.NasFileCopy/rsync-logs";
  private const string Sep = " /// ";
  /// <summary>
  /// <a href="https://linux.die.net/man/5/rsyncd.conf#:~:text=The%20single%2Dcharacter%20escapes%20that%20are%20understood%20are%20as%20follows">See man rsyncd.conf(5)</a>
  /// </summary>
  /// <example>
  /// <code>
  /// 2025/03/03 13:52:17 [68684] receiving file list
  /// 2025/03/03 13:52:17 [68684] delta-transmission disabled for local transfer or --whole-file
  /// 2025/03/03 13:52:17 [68684] ./
  /// 2025/03/03 13:52:17 [68684] A/
  /// 2025/03/03 13:52:17 [68684]  /// Users/samuel/dest/.DS_Store /// 10284 /// 10244 /// 10244 /// 68685
  /// 2025/03/03 13:52:17 [68684] A/1/
  /// 2025/03/03 13:52:22 [68684]  /// Users/samuel/dest/TOPO-MOOKAR-02.cdr /// 631342376 /// 631265280 /// 631265280 /// 68685
  /// 2025/03/03 13:52:26 [68684]  /// Users/samuel/dest/TOPO-MOOKAR-03 copy.cdr /// 641688088 /// 641609728 /// 641609728 /// 68685
  /// 2025/03/03 13:52:37 [68684]  /// Users/samuel/dest/TrueNAS-SCALE-24.04.1.1 copy 2.iso /// 1628721328 /// 1628522496 /// 1628522496 /// 68685
  /// 2025/03/03 13:52:49 [68684]  /// Users/samuel/dest/TrueNAS-SCALE-24.04.1.1.iso /// 1628721328 /// 1628522496 /// 1628522496 /// 68685
  /// 2025/03/03 13:52:49 [68684]  /// Users/samuel/dest/txt.txt /// 43 /// 3 /// 3 /// 68685
  /// 2025/03/03 13:53:08 [68684]  /// Users/samuel/dest/ubuntu-24.04.1-live-server-amd64 copy 2.iso /// 2774213332 /// 2773874688 /// 2773874688 /// 68685
  /// 2025/03/03 13:53:29 [68684]  /// Users/samuel/dest/ubuntu-24.04.1-live-server-amd64 copy.iso /// 2774213332 /// 2773874688 /// 2773874688 /// 68685
  /// 2025/03/03 13:53:29 [68684]  /// Users/samuel/dest/A/.DS_Store /// 6188 /// 6148 /// 6148 /// 68685
  /// 2025/03/03 13:53:29 [68684]  /// Users/samuel/dest/A/1/.DS_Store /// 6188 /// 6148 /// 6148 /// 68685
  /// 2025/03/03 13:53:49 [68684]  /// Users/samuel/dest/A/1/ubuntu-24.04.1-live-server-amd64 copy 2 12.50.29.iso /// 2774213332 /// 2773874688 /// 2773874688 /// 68685
  /// 2025/03/03 13:53:49 [68684] sent 1927 bytes  received 1593 bytes  total size 19828295678
  /// </code>
  /// <code>
  /// 2025/03/03 12:03:54 [67377] rsync: write failed on "/this/is/a/path/file.txt": No space left on device (28)
  /// 2025/03/03 12:03:54 [67377] rsync error: error in file IO (code 11) at /some/path/to/a/source/rsync/rsync/receiver.c(268) [receiver=2.6.9]
  /// 2025/03/03 12:03:54 [67377] rsync: connection unexpectedly closed (1026 bytes received so far) [generator]
  /// 2025/03/03 12:03:54 [67377] rsync error: error in rsync protocol data stream (code 12) at /some/path/to/a/source/rsync/rsync/io.c(453) [generator=[2.6.9]
  /// </code>
  /// </example>
  private const string LogFmt = $"{Sep}%p{Sep}%b{Sep}%i{Sep}%f{Sep}";
  private const string StdArgs = $"--verbose --archive --no-o --no-g --times  --log-file-format=\"{LogFmt}\"  --stats -P";

  /// <summary>Captures an entire log line's datetime/pid and contents. Use the other `Re`s to match specific lines.</summary>
  private static readonly Regex LogPrefixRe = new(@"^(?<datetime>[\d/]{10} [\d:]{8}) \[(?<boxedpid>\d+)\] (?<contents>[^\n]*)$", RegexOptions.Multiline);
  /// <summary>Matches the contents of a transfer log line.</summary>
  private static readonly Regex LogRe = new(@"^ /// (?<pid>\d+) /// (?<fileBytes>\d+) /// (?<itemizedChanges>\S+) /// (?<fileName>.*) /// $");
  /// <summary>Matches the contents of a final log line with sent/received/total bytes.</summary>
  private static readonly Regex FinalLogRe2 = new(@"^(?!.*(///|rsync|:).*$)(sent |received |total size |speedup).*?  $");
  // private static readonly Regex FinalLogRe = new(@"^sent\s+(?<sent>[\d,]+)\s+bytes\s+received(?<recv>[\d,]+)\s+bytes\s+total size\s+(?<total>\d+)$");
  /// <summary>Matches the contents of a log from rsync itself.</summary>
  private static readonly Regex MetaLogRe = new(@"^rsync[: ]+(?<meta>[^\n]+)$");

  private static readonly Regex TwoOrMoreSpacesRe = new(@"  +");
  private static readonly Regex NoColonLabelNumberRe = new(@"(?<=[a-z])\s+(?=[0-9])");

  private readonly TrueNasSshClient _client;
  private readonly CancellationTokenSource _cancellationTokenSource = new();

  private uint? ProcessId { get; set; }

  public string RunId { get; }
  public string LogFilePath { get; }
  public string LockFileBaseName { get; }
  public string CopyFrom { get; }
  public string Destination { get; }
  public RsyncLogState? CurrentState { get; private set; }
  public ImmutableDictionary<string, string>? FinalLogs { get; private set; }
  public bool Completed => CurrentState?.CompletedSuccessfully is not null;

  public TimeSpan ListenDelay { get; set; } = TimeSpan.FromSeconds(1);
  public uint EmptyListenDelaysBeforeRecheck { get; set; } = 5;

  private RsyncLogReader(
    string runId,
    TrueNasSshClient client,
    string copyFrom,
    string destination,
    CancellationToken cancellationToken
  )
  {
    Debug.Assert(copyFrom[0] is '\'');
    Debug.Assert(destination[0] is '"');

    _client = client;

    RunId = runId;
    LogFilePath = $"{RsyncLogDir}/{RunIdToLogFileName(RunId)}";
    LockFileBaseName = RunIdToLockFileName(RunId);

    CopyFrom = copyFrom;
    Destination = destination;

    cancellationToken.Register(_cancellationTokenSource.Cancel);
  }

  public const string RunIdDateFormat = "yyyyMMddHHmmss";
  public static string RunIdToLockFileName(string runId) => $"rsync.{runId}.lock";
  public static string RunIdToLogFileName(string runId) => $"rsync.{runId}.log";
  public static void ValidateRunId(string runId) => _ = DateTime.ParseExact(runId, RunIdDateFormat, null);

  public string GetCommandText() => $"sudo rsync {StdArgs} --log-file=\"{LogFilePath}\" {CopyFrom} {Destination}";

  public static async Task<RsyncLogReader> CreateAndStartNewAsync(
    ISudoPrompter sudoPrompter,
    TrueNasSshClient client,
    string copyFrom,
    string destination,
    CancellationToken cancellationToken
  )
  {
    var @this = new RsyncLogReader(
      runId: DateTime.Now.ToString(RunIdDateFormat),
      client,
      copyFrom: copyFrom,
      destination: destination,
      cancellationToken
    );

    await @this.StartNewAsync(sudoPrompter);
    return @this;
  }

  private static (string copyFrom, string destination, string logFile)? ParseCommandLineArgs(string[] commandArgs)
  {
    if (commandArgs is ["sudo", "rsync", .., string logCmd, string copyFrom, string destination] && logCmd.StartsWith("--log-file="))
      return (copyFrom, destination, logCmd["--log-file=".Length..]);
    return null;
  }

  /// <summary>Create a new log reader attaching to a running reader.</summary>
  /// <exception cref="RsyncException">If cannot find lock file for for <paramref name="runId"/>.</exception>
  /// <exception cref="RsyncProcessNotFoundException">If cannot read process folder (i.e. process probably doesn't exist).</exception>
  public static async Task<RsyncLogReader> PickUpExistingAsync(
    TrueNasSshClient sshClient,
    string runId,
    CancellationToken cancellationToken
  )
  {
    var lockFile = await ReadFromLockFileGivenAsync(runId, sshClient, cancellationToken);

    var commandArgs = await ReadCmdlineForGivenProcess(sshClient, lockFile.processId, cancellationToken);

    if (ParseCommandLineArgs(commandArgs) is not (string copyFrom, string destination, _))
      throw new RsyncProcessNotFoundException($"{nameof(PickUpExistingAsync)} found PID={lockFile.processId} for runId={runId}, but it wasn't one of our rsync runs: {string.Join('\t', commandArgs)}");

    if (copyFrom != lockFile.copyFrom || destination != lockFile.destination)
      throw new RsyncProcessNotFoundException($"{nameof(PickUpExistingAsync)} found PID={lockFile.processId} for runId={runId}, and it was one of our rsync runs, but not the same copyFrom/destination: {string.Join('\t', commandArgs)}");

    var @this = new RsyncLogReader(
      runId: runId,
      sshClient,
      $"'{copyFrom}'",
      $"\"{destination}\"",
      cancellationToken
    );
    @this.ProcessId = lockFile.processId;

    return @this;
  }

  // private const string IdFinderOutput = "<${?}=${!}>";
  // private static readonly Regex IdFinderRe = new(@"<(?<exit>\d+)=(?<pid>\d+)>");
  private async Task StartNewAsync(ISudoPrompter sudoPrompter)
  {
    if (ProcessId.HasValue)
      throw new InvalidOperationException("Re-execution");

    var token = _cancellationTokenSource.Token;

    using (var mkdirLogCmd = _client.SshClient!.CreateCommand($"mkdir -p {RsyncLogDir}"))
    {
      await mkdirLogCmd.ExecuteAsync(token);
      if (mkdirLogCmd.ExitStatus is not 0)
        throw new RsyncException($"Failed to create {RsyncLogDir}");
    }

    // TODO: cannot do just &; we need to disown
    // sudo cmd &


    // sudo -k && sudo --stdin --validate --prompt='TNSC_SUDO_PROMPT'
    // sudo rsync --log-file="$HOME/Org.Grush.NasFileCopy/rsync-logs/test-rsync.log" --verbose --archive --no-o --no-g --times  --log-file-format=" /// %p /// %b /// %i /// %f /// "  --stats -P   /mnt/KenFS/CaveFS/    ~/mnt/MSSarchives/ & ; echo "<${?}=${!}>"

    var cmdResult = await _client.CallSudoCommand<(byte exitCode, uint pid)>(
      commands: [
        (
          command: $"{GetCommandText()} &",
          act: rsyncResult => rsyncResult.exitStatus is 0
        ),
        (
          command: "disown",
          act: null
        )
      ],
      handler: (shellStream, output, lastCode, noneStopped) => (
        commentary: null,
        succeeded: noneStopped,
        result: (
          exitCode: lastCode.exitStatus,
          pid: lastCode.pid
        )
      ),
      sudoPrompter: sudoPrompter,
      cancellationToken: token
    );

    if (!cmdResult.Success)
      throw cmdResult.Exception!;

    (byte exitCode, uint pid) = cmdResult.Result;

    if (exitCode is not 0)
      throw new RsyncCommandFailedException(exitCode, $"{nameof(StartNewAsync)} failed with output: {cmdResult.Output}|{cmdResult.Error}");
    if (pid is 0)
      throw new RsyncCommandFailedException(0, $"{nameof(StartNewAsync)} returned 0 but pid is 0.");
    ProcessId = pid;

    await CreateLockFileAsync();
  }

  public async IAsyncEnumerable<RsyncLogState> ListenAsync(Func<string, Task> errorLogger, [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var (processIdFromLockFile, _, _) = await ReadFromLockFileAsync();

    if (ProcessId != processIdFromLockFile)
      throw new RsyncException($"Mismatched pids: {ProcessId}|{processIdFromLockFile}");

    using var scopedToken = new CancellationTokenSource();
    var token = scopedToken.Token;

    _cancellationTokenSource.Token.Register(scopedToken.Cancel);
    cancellationToken.Register(scopedToken.Cancel);

    token.ThrowIfCancellationRequested();

    const int capacity = 4 * 1024;

    using var tailCommand = _client.SshClient!.CreateCommand($"tail --pid {ProcessId} -c {capacity} -f \"{LogFilePath}\"");
    var executeTask = tailCommand.ExecuteAsync(token);

    string currentState = "";
    int iterationsSinceNoOutput = 0;

    await using var bufferedStream = new BufferedStream(tailCommand.OutputStream, capacity);
    while (true) // breaks if reaches the end OR in the delay at the end
    {

      // read into the string until we hit the end
      int bytesRead = 0;
      int bytesReadThisLoop;
      var bytes = new byte[capacity];
      var mem = bytes.AsMemory(0, capacity);
      do
      {
        bytesReadThisLoop = await bufferedStream.ReadAsync(mem, token);
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
          {
            // unexpectedly, we started a new iteration with a closed command of some kind
            RsyncLogState rsyncLogState = (CurrentState ?? RsyncLogState.Empty) with
              {
                CompletedSuccessfully = tailCommand.ExitStatus switch
                {
                  null => null,
                  0 => true,
                  _ => false,
                }
              };
            CurrentState = rsyncLogState;
            if (rsyncLogState.CompletedSuccessfully is not null)
              await DeleteLockFileAsync();

            yield return rsyncLogState;
            yield break;
          }

          iterationsSinceNoOutput = 0;
        }
      }
      else
      {
        iterationsSinceNoOutput = 0;

        int nextUnreadIdx = 0;
        foreach (Match lineMatch in LogPrefixRe.Matches(currentState))
        {
          var lineContents = lineMatch.Groups["contents"].Value;

          if (LogRe.Match(lineContents) is { Success: true } logMatch)
          {
            var fileBytes = ulong.Parse(logMatch.Groups["fileBytes"].Value);
            var total = (CurrentState?.TotalKnownBytesTransmitted ?? 0) + fileBytes;
            RsyncLogState rsyncLogState = new(
              LatestTransmittedFile: logMatch.Groups["fileName"].Value,
              TotalKnownBytesTransmitted: total,
              LatestTransmittedFileBytes: fileBytes,
              CompletedSuccessfully: null
            );
            CurrentState = rsyncLogState;
            yield return rsyncLogState;
          }
          else if (FinalLogRe2.Match(lineContents) is { Success: true } finalLogMatch)
          {
            FinalLogs = ReadFinalLogs(currentState);

            var totalBytesTransmitted = ulong.Parse(
              FinalLogs.GetValueOrDefault("total size", "0"),
              NumberStyles.Float | NumberStyles.AllowThousands
            );

            RsyncLogState rsyncLogState = new(
              LatestTransmittedFile: "",
              LatestTransmittedFileBytes: totalBytesTransmitted,
              TotalKnownBytesTransmitted: CurrentState?.TotalKnownBytesTransmitted ?? 0,
              CompletedSuccessfully: true
            );
            CurrentState = rsyncLogState;

            await DeleteLockFileAsync();

            yield return rsyncLogState;
            yield break;
          }
          else if (MetaLogRe.Match(lineContents) is { Success: true } metaLogMatch)
          {
            await errorLogger(metaLogMatch.Groups["meta"].Value);
          }
          // else ignored line

          nextUnreadIdx = lineMatch.Index + lineMatch.Length;
        }

        // trim off everything we've read
        currentState = currentState[nextUnreadIdx..];
      }

      await Task.Delay(ListenDelay, token);
    }
  }

  private async Task<(uint processId, string copyFrom, string destination)> ReadFromLockFileAsync()
    => await ReadFromLockFileGivenAsync(RunId, _client, _cancellationTokenSource.Token);

  private static ImmutableDictionary<string, string> ReadFinalLogs(string text)
  {
    return text
      .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
      // find all the final lines
      .Select(line => FinalLogRe2.Match(line))
      .Where(match => match.Success)
      // further split final lines into their separate properties
      .SelectMany(match => TwoOrMoreSpacesRe.Split(match.Value))
      .Select<string, KeyValuePair<string, string>>((entry, idx) =>
      {
        if (entry.Split(" is ", 2, StringSplitOptions.TrimEntries) is [string isKey, string isValue])
          return KeyValuePair.Create(isKey, isValue);
        if (entry.Contains("bytes/s"))
          return KeyValuePair.Create($"rate-{idx}", entry);
        if (NoColonLabelNumberRe.Split(entry, 2) is [string lLabel, string lNumber])
          return KeyValuePair.Create(lLabel, lNumber);
        return KeyValuePair.Create($"unknown-{idx}", entry);
      })
      .ToImmutableDictionary();
  }

  /// <summary>
  /// Read the contents of one of our lockfiles.
  /// </summary>
  /// <returns>tuple read from the lock file.</returns>
  /// <exception cref="RsyncException">If the lockfile is not found or is not valid.</exception>
  public static async Task<(uint processId, string copyFrom, string destination)> ReadFromLockFileGivenAsync(string runId, TrueNasSshClient client, CancellationToken cancellationToken)
  {
    // validate runId
    ValidateRunId(runId);

    var lockFileResult = await client.ReadFileAsync($"{RsyncLogDir}/{RunIdToLockFileName(runId)}", cancellationToken: cancellationToken);

    if (lockFileResult.Success)
      throw new RsyncException($"Failed to {nameof(ReadFromLockFileGivenAsync)} for {runId}");

    if (lockFileResult.Result.Item1.Split('\n', StringSplitOptions.RemoveEmptyEntries) is not [string pid, string copyFrom, string destination])
      throw new RsyncException(
        $"Failed to parse lockfile in {nameof(ReadFromLockFileGivenAsync)} for {runId}: {lockFileResult.Result.Item1}");
    return (
      processId: uint.Parse(pid),
      copyFrom: copyFrom,
      destination: destination
    );
  }

  private async Task CreateLockFileAsync()
  {
    if (!ProcessId.HasValue)
      throw new ArgumentException($"Cannot {nameof(CreateLockFileAsync)} before started.");

    var quotedLockFilePath = $"\"{RsyncLogDir}/{LockFileBaseName}\"";
    var token = _cancellationTokenSource.Token;

    using (var checkFileExistsCmd = _client.SshClient!.CreateCommand($"[ -f {quotedLockFilePath} ]"))
    {
      await checkFileExistsCmd.ExecuteAsync(token);

      if (checkFileExistsCmd.ExitStatus is 0)
        throw new RsyncException(
          $"Lock file {quotedLockFilePath} already exists in {nameof(RsyncLogDir)} while attempting to {nameof(CreateLockFileAsync)}");
    }

    string lockFileContents = $"""
      {ProcessId.Value}
      {CopyFrom}
      {Destination}
      """;

    using var createCmd = _client.SshClient!.CreateCommand($"echo \"{lockFileContents}\" >{quotedLockFilePath}");
    await createCmd.ExecuteAsync(cancellationToken: token);

    if (createCmd.ExitStatus is not 0)
      throw new RsyncException($"Failed to {nameof(CreateLockFileAsync)}; exit status {createCmd.ExitStatus}");
  }

  private async Task DeleteLockFileAsync()
  {
    await DeleteLockFileAsync(_client, RunId, _cancellationTokenSource.Token);
  }

  public static async Task DeleteLockFileAsync(TrueNasSshClient client, string runId, CancellationToken cancellationToken)
  {
    ValidateRunId(runId);

    using var rmCmd = client.SshClient!.CreateCommand($"rm \"{RsyncLogDir}/{RunIdToLogFileName(runId)}\" \"{RsyncLogDir}/{RunIdToLockFileName(runId)}\" ");
    await rmCmd.ExecuteAsync(cancellationToken: CancellationToken.None);

    if (rmCmd.ExitStatus is not 0)
      throw new RsyncException($"Failed to {nameof(DeleteLockFileAsync)}; exit status {rmCmd.ExitStatus}");
  }

  private async Task<string[]> ReadCmdlineForProcess(TrueNasSshClient client)
  {
    if (ProcessId is not uint processId)
      throw new RsyncException($"Attempt to {nameof(ReadCmdlineForProcess)} before {nameof(StartNewAsync)}");

    return await ReadCmdlineForGivenProcess(client, processId: processId, _cancellationTokenSource.Token);
  }

  public static async Task<string[]> ReadCmdlineForGivenProcess(TrueNasSshClient client, uint processId, CancellationToken token)
  {
    var cmdLineResult = await client.ReadFileAsync($"/proc/{processId}/cmdline", cancellationToken: token);
    if (!cmdLineResult.Success)
      throw new RsyncProcessNotFoundException($"Proc folder for {processId} cmdline not found");

    var parts = cmdLineResult.Result.Item1.Trim('\n').Split('\0');
    if (parts.Last() is "")
      return parts[..^1];

    throw new InvalidOperationException("cmdline file not ending in NUL");
  }

  /// <summary>
  /// Read all status contents for the process, splitting on the colon and tab.
  /// </summary>
  /// <param name="client"></param>
  /// <returns></returns>
  /// <exception cref="RsyncException"></exception>
  /// <exception cref="RsyncProcessNotFoundException"></exception>
  private async Task<ImmutableDictionary<string, string>> ReadStatsForProcess(TrueNasSshClient client)
  {
    if (ProcessId is not uint processId)
      throw new RsyncException("ProcessId not yet");

    return await ReadStatsForProcess(client, processId, _cancellationTokenSource.Token);
  }

  /// <summary>
  /// Read all the stats from the <c>/proc/{PID}/status</c> file for the given process as a dictionary.
  /// </summary>
  /// <exception cref="RsyncProcessNotFoundException">If the file cannot be read.</exception>
  private static async Task<ImmutableDictionary<string, string>> ReadStatsForProcess(TrueNasSshClient client, uint pid, CancellationToken cancellationToken)
  {
    var statusResult = await client.ReadFileAsync($"/proc/{pid}/status", cancellationToken: cancellationToken);
    if (!statusResult.Success)
      throw new RsyncProcessNotFoundException($"Proc folder for {pid} status not found");

    return statusResult.Result
        .Item1
        .Split('\n')
        .Select(line => line.Split(":\t", 2))
        .Where(pair => pair.Length is 2)
        .ToImmutableDictionary(pair => pair[0], pair => pair[1]);
  }

  public static async Task<ImmutableArray<(string runId, LsLine)>> GetExistingLockFileNamesAsync(TrueNasSshClient client, CancellationToken cancellationToken)
  {
    var quotedLockFileDir = $"\"{RsyncLogDir}/\"";

    var list = await client.LsVerboseAsync(quotedLockFileDir, cancellationToken: cancellationToken);

    if (list.ExitStatus > 0)
      return []; // the log dir doesn't even exist

    return [
      ..list.Result
        .Select(line => line.Name.Split('.') is ["rsync", string runId, "lock"] ? (runId, line) : (null!, line))
        .Where(pair => pair.runId is not null)
    ];
  }

  public static async Task<ImmutableArray<ExistingRun>> GetExistingRunsAsync(TrueNasSshClient client, CancellationToken cancellationToken)
  {
    var lockFiles = await GetExistingLockFileNamesAsync(client, cancellationToken: cancellationToken);

    List<ExistingRun> runs = new(lockFiles.Length);
    foreach (var (runId, lsLine) in lockFiles)
    {
      string? copyFrom = null;
      string? destination = null;
      string? processState = null;
      try
      {
        (var procId, copyFrom, destination) = await ReadFromLockFileGivenAsync(runId, client, cancellationToken);

        var stats = await ReadStatsForProcess(client, procId, cancellationToken);

        processState = stats.GetValueOrDefault("State", "[Found]");
      }
      catch (RsyncException)
      {
      }

      runs.Add(new (RunId: runId, ProcessState: processState, CopyFrom: copyFrom, Destination: destination)); //, Line: lsLine));
    }

    return [..runs];
  }

  ValueTask IAsyncDisposable.DisposeAsync()
  {
    _cancellationTokenSource.Cancel();
    _cancellationTokenSource.Dispose();

    return default;
  }

  #region structures

  public class RsyncException(string message) : Exception(message);
  public class RsyncProcessNotFoundException(string message) : RsyncException(message);

  public class RsyncCommandFailedException(byte exitCode, string? message)
    : RsyncException($"rsync exited with {exitCode}{(message is null ? "" : $"; {message}")}");

  #endregion
}




/// <param name="RunId">The <see cref="RsyncLogReader.RunId"/> of the run.</param>
/// <param name="ProcessState">
/// State read from the process's status file, as described in <a href="https://man7.org/linux/man-pages/man5/proc_pid_status.5.html">man proc_pid_status</a> <c>Status</c>.
/// <c>null</c> if process couldn't be found or lockfile not read.
/// </param>
/// <param name="CopyFrom">The <see cref="RsyncLogReader.CopyFrom"/> of the run from the lockfile.</param>
/// <param name="Destination">The <see cref="RsyncLogReader.Destination"/> of the run from the lockfile.</param>
/// <param name="Line">The <see cref="LsLine"/> of the lockfile.</param>
public readonly record struct ExistingRun(
  string RunId,
  string? ProcessState,
  string? CopyFrom,
  string? Destination
  // , LsLine Line
);


/// <summary>Rsync state.</summary>
/// <param name="LatestTransmittedFile">Name of the latest transmitted file, <c>""</c> if complete, or <c>null</c> if no file emitted.</param>
/// <param name="LatestTransmittedFileBytes">Size the latest file in bytes, or final transmitted bytes if complete.</param>
/// <param name="TotalKnownBytesTransmitted">Total known bytes transmitted.</param>
/// <param name="CompletedSuccessfully"><c>null</c> when incomplete, else <c>true</c> or <c>false</c> for successful completion.</param>
public readonly record struct RsyncLogState(
  string? LatestTransmittedFile,
  ulong LatestTransmittedFileBytes,
  ulong TotalKnownBytesTransmitted,
  bool? CompletedSuccessfully
)
{
  internal static readonly RsyncLogState Empty = new(null, 0, 0, null);
}