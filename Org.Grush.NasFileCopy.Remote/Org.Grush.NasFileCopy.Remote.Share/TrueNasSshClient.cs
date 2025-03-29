using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Org.Grush.NasFileCopy.Remote.Share.Exceptions;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Org.Grush.NasFileCopy.Remote.Share.Validation;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share;

public class DangerousOperationException(string message, string paramName) : ArgumentException(message, paramName)
{
  public DangerousOperationException(string what, IEnumerable<string> errors, string paramName)
    : this($"{what}: {string.Join(", ", errors)}", paramName)
  {
  }
}

public class SudoFailedException(string msg) : Exception($"Sudo failed: {msg}");

internal sealed class TrueNasSshClient(
  IAnalyticsReporter analyticsReporter
) : IAsyncDisposable
{
  internal SshClient? SshClient { get; private set; }

  public bool IsDisposed { get; private set; }
  public bool IsConnected => SshClient?.IsConnected ?? false;

  public async Task ConnectAsync(
    ConnectionInfo sshCredentials,
    CancellationToken cancellationToken
  )
  {
    if (SshClient is { IsConnected: true })
      throw new InvalidOperationException("You already connected.");

    if (SshClient is null || !SshClient.ConnectionInfo.Equals(sshCredentials))
      SshClient = new SshClient(sshCredentials);

    await ReconnectIfNeededAsync(cancellationToken);
  }

  public void Disconnect()
  {
    SshClient?.Disconnect();
  }

  public async Task ReconnectIfNeededAsync(CancellationToken cancellationToken)
  {
    if (SshClient is null)
      throw new InvalidOperationException("Connect to reconnect.");
    if (SshClient.IsConnected)
      return;

    await SshClient.ConnectAsync(cancellationToken);
  }

  public async Task<SshResult<bool>> UnmountAsync(ISudoPrompter? sudoPrompter, string devPath, CancellationToken cancellationToken) =>
    sudoPrompter is null
    ? await CallCommand<bool>(
      preconditions: null,
      getCommand: () => $"umount '{ProcessSingleQuotePath(devPath, nameof(devPath))}'",
      handler: command =>
      {
        if (command.ExitStatus is 0)
          return (null, true, true);

        analyticsReporter.LogError("Unmount device exited with {status}", command.ExitStatus);
        return ($"Error: unmount device exit status {command.ExitStatus}", false, null);
      },
      cancellationToken: cancellationToken
    )
    :
    await CallSudoCommand<bool>(
      sudoPrompter: sudoPrompter,
      commands: [
        (
          command: $"sudo umount '{ProcessSingleQuotePath(devPath, nameof(devPath))}'",
          act: null
        )
      ],
      handler: (shellStream, finalOutput, codes, noneFailed) =>
      {
        if (codes.exitStatus is 0)
          return (null, true, true);

        analyticsReporter.LogError("Unmount device exited with {status}", codes.exitStatus);
        return ($"Error: unmount device exit status {codes.exitStatus}", false, null);
      },
      cancellationToken: cancellationToken
    );

  public async Task<SshResult<ImmutableArray<MountListLine>>> MountListAsync(CancellationToken cancellationToken) =>
    await CallCommand<ImmutableArray<MountListLine>>(
      preconditions: null,
      getCommand: () => "mount",
      handler: command =>
      {
        if (command.ExitStatus is not 0)
        {
          analyticsReporter.LogError("Mount list error: exited {status}, error: {error}", command.ExitStatus, command.Error);
          return ($"Error: mount list exit status {command.ExitStatus}", false, null);
        }

        var result = command.Result;
        var mountList = MountListLine.ReadLines(result).ToImmutableArray();

        if (mountList.Length is 0 && result.Count(c => c is '\n') is var newlineCount and > 2)
        {
          analyticsReporter.LogError("Mount list parse fail: {newlineCount} newlines, exited {status}, output: {output}", newlineCount, command.ExitStatus, result);
          return ($"Error: parsed no mounts but result seems to have {newlineCount - 1} lines??", false, mountList);
        }

        return (null, true, mountList);
      },
      cancellationToken: cancellationToken
    );

  public async Task<SshResult<bool>> MountDeviceAsync(ISudoPrompter sudoPrompter, string devPath, string destinationPath, CancellationToken cancellationToken) =>
    await CallSudoCommand<bool>(
      commands: [
        (
          command: $"sudo mount -o user -rw '{ProcessSingleQuotePath(devPath, nameof(devPath))}' \"{ProcessDoubleQuotePath(destinationPath, nameof(destinationPath))}\"",
          /*expectation: new Regex(@"$ "),*/
          act: null
        )
      ],
      handler: (shellStream, totalOutput, lastCode, noneStopped) =>
      {
        var exitCode = lastCode.exitStatus;
        if (exitCode is 0)
          return (null, true, true);

        analyticsReporter.LogError("Mount device exited with {status}", exitCode);
        return ($"Error: mount device exit status {exitCode}", false, null);
      },
      sudoPrompter: sudoPrompter,
      cancellationToken: cancellationToken
    );

  public async Task<SshResult<ValueTuple<string>>> ReadFileAsync(string filePath, CancellationToken cancellationToken) =>
    await CallCommand<ValueTuple<string>>(
      preconditions: () =>
      {
        if (PathValidation.IsInvalidatePathEntry(filePath, out var errors, doubleQuotable: true))
          throw new DangerousOperationException("contains dangerous characters.", errors, nameof(filePath));
      },
      getCommand: () => $"cat \"{filePath}\"",
      handler: command =>
      {
        if (command.ExitStatus is 0)
          return (null, true, ValueTuple.Create(command.Result));

        analyticsReporter.LogError("cat exited with {status}", command.ExitStatus);
        return ($"cat exited unexpectedly with {command.ExitStatus}", false, null);
      },
      cancellationToken: cancellationToken
    );

  /// <summary>
  /// Return a simple list of files/folders/etc in the directory <paramref name="path"/>.
  /// Does not return <c>.</c> nor <c>..</c>.
  /// </summary>
  public async Task<SshResult<ImmutableArray<string>>> LsAsync(string path, CancellationToken cancellationToken) =>
    await CallCommand<ImmutableArray<string>>(
      preconditions: () =>
      {
        PathValidation.ReplaceHomeTilde(ref path);
        if (PathValidation.IsInvalidatePathEntry(path, out var errors, doubleQuotable: true))
          throw new DangerousOperationException("contains dangerous characters.", errors, nameof(path));
      },
      getCommand: () => $"ls -1 \"{path}\" ",
      handler: command =>
      {
        if (command.ExitStatus is 0)
          return (null, true, [
            ..command.Result
              .TrimEnd('\n')
              .Split('\n')
          ]);

        analyticsReporter.LogError("ls exited with {status}", command.ExitStatus);
        return ($"ls exited unexpectedly with {command.ExitStatus}", false, null);

      },
      cancellationToken: cancellationToken
    );

  /// <summary>
  /// Return the results of <c>ls -al</c> on the directory <paramref name="path"/>,
  /// including <c>.</c> and <c>..</c>, with metadata about entries.
  /// </summary>
  public async Task<SshResult<ImmutableArray<LsLine>>> LsVerboseAsync(string path, CancellationToken cancellationToken) =>
    await CallCommand<ImmutableArray<LsLine>>(
      preconditions: () =>
      {
        PathValidation.ReplaceHomeTilde(ref path);

        if (PathValidation.IsInvalidatePathEntry(path, out var errors, doubleQuotable: true))
          throw new DangerousOperationException("contains dangerous characters.", errors, nameof(path));
      },
      getCommand: () => $"ls {LsLine.LsFlags} \"{path}\" ",
      handler: command =>
      {
        if (command.ExitStatus is 0)
        {
          return (null, true, [
            ..LsLine.Parse(command.Result)
          ]);
        }

        analyticsReporter.LogError("ls exited with {status}", command.ExitStatus);
        return ($"ls exited unexpectedly with {command.ExitStatus}", false, null);

      },
      cancellationToken: cancellationToken
    );

  public async Task<SshResult<LsblkDevice.LsblkResult>> LsblkAsync(CancellationToken cancellationToken)
    => await CallCommand(
      preconditions: null,
      getCommand: () => $"lsblk {LsblkDevice.Options}",
      handler: command =>
      {
        if (command.ExitStatus is 0)
        {
          var v = JsonSerializer.Deserialize(command.Result,
            LsblkDeviceJsonSerializerContext.Default.LsblkResult);
          return (null, true, v);
        }

        return ($"lsblk exited unexpectedly with {command.ExitStatus}", false, (LsblkDevice.LsblkResult?)null);
      },
      cancellationToken: cancellationToken
    );

  public async Task<RsyncLogReader> Rsync(
    ISudoPrompter sudoPrompter,
    string copyFrom,
    string destination,
    CancellationToken cancellationToken
  )
  {
    if (PathValidation.IsInvalidatePathEntry(copyFrom, out var sqErrors, singleQuotable: true, notEmpty: true, noVariables: true))
      throw new DangerousOperationException("contains dangerous characters", sqErrors, nameof(copyFrom));
    if (PathValidation.IsInvalidatePathEntry(destination, out var dqErrors, doubleQuotable: true))
      throw new DangerousOperationException("contains dangerous characters", dqErrors, nameof(destination));
    if (SshClient?.IsConnected is not true)
      throw new InvalidOperationException("Connect first");

    return await RsyncLogReader.CreateAndStartNewAsync(
      sudoPrompter: sudoPrompter,
      client: this,
      copyFrom: $"'{copyFrom}/'", // add trailing slash to hopefully get only the contents; seems to work
      destination: $"\"{destination}\"",
      cancellationToken: cancellationToken
    );
  }

  ValueTask IAsyncDisposable.DisposeAsync()
  {
    SshClient?.Dispose();
    SshClient = null;

    IsDisposed = true;

    return default;
  }




  private string ProcessDoubleQuotePath(string path, string name)
  {
    PathValidation.ReplaceHomeTilde(ref path);

    if (PathValidation.IsInvalidatePathEntry(path, out var errors, doubleQuotable: true))
      throw new DangerousOperationException("contains dangerous characters.", errors, name);

    return path;
  }

  private string ProcessSingleQuotePath(string path, string name)
  {
    if (PathValidation.IsInvalidatePathEntry(path, out var errors, singleQuotable: true, notEmpty: true, noVariables: true))
      throw new DangerousOperationException("contains dangerous characters.", errors, name);
    return path;
  }

  private async Task<SshResult<T>> CallCommand<T>(
    Action? preconditions,
    Func<string> getCommand,
    Func<SshCommand, (string? commentary, bool succeeded, T? result)> handler,
    CancellationToken cancellationToken
  )
    where T : struct
  {
    try
    {
      if (SshClient is null)
        throw new InvalidOperationException("Connect first");

      preconditions?.Invoke();

      using var command = SshClient.CreateCommand(getCommand());
      await command.ExecuteAsync(cancellationToken);

      (string? commentary, bool succeeded, T? result) = handler(command);

      return SshResult<T>.Completed(success: succeeded, result, command) with { Commentary = commentary };
    }
    catch (Exception e)
    {
      return SshResult<T>.Threw(e);
    }
  }

  internal async Task<SshResult<T>> CallSudoCommand<T>(
    ISudoPrompter sudoPrompter,
    IEnumerable<(string command, /*Regex expectation,*/ Func<(string output, byte exitStatus, ushort pid), bool>? act)> commands,
    Func<ShellStream, string?, (byte exitStatus, ushort pid), bool, (string? commentary, bool succeeded, T? result)> handler,
    CancellationToken cancellationToken
  )
    where T : struct
  {
    try
    {
      if (SshClient is not { IsConnected: true })
        throw new InvalidOperationException("Connect first");

      await using var shellStream = SshClient.CreateShellStreamNoTerminal();

      // wait for the shell prompt
      var expectedShell = shellStream.Expect("$ ");
      if (expectedShell is null)
        throw new SudoFailedException("Stream closed before shell appeared");

      // prompt password
      var password = await sudoPrompter.GetPasswordAsync();
      if (password is null or "")
        throw new PromptCancelledException("Sudo prompt cancelled");

      // clear sudo cache and read in password from prompt
      shellStream.WriteLine("sudo -k ; sudo --stdin --validate --prompt='TNSC_SUDO_PROMPT' ");
      var promptMatch = shellStream.Expect(new Regex("TNSC_SUDO_PROMPT|.+"));
      if (promptMatch != "TNSC_SUDO_PROMPT")
        throw new SudoFailedException("Timeout?");
      shellStream.WriteLine(password);

      var passwordSubmitMatch = shellStream.Expect(new Regex(@"\$|Sorry, try again"));
      if (passwordSubmitMatch is null)
        throw new SudoFailedException("Timeout?");
      if (passwordSubmitMatch is "Sorry, try again")
        throw new SudoFailedException("Password rejected");

      List<(byte exitCode, ushort pid)> codes = [];

      bool noneStopped = true;

      foreach (var (command, /*expectation,*/ callback) in commands)
      {
        shellStream.Seek(0, SeekOrigin.End);
        var startMark = Guid.NewGuid().ToString("N");
        var endMark = Guid.NewGuid().ToString("N");
        var outputFinder = new Regex(startMark + @"(?<output>.*)\[(?<exitCode>\d+),(?<pid>\d+)\]" + endMark, RegexOptions.Singleline);

        shellStream.WriteLine($"echo {startMark}; {command} ; echo \"[${{?}},${{!}}]{endMark}\"");

        var resultOutput = shellStream.Expect(outputFinder);
        if (resultOutput is null)
          throw new SudoFailedException("Timeout? on command");

        var resultMatch = outputFinder.Match(resultOutput);

        if (!resultMatch.Success)
          throw new SudoFailedException("match but mismatch");

        var output = resultMatch.Groups["output"].Value;
        var exitCode = byte.Parse(resultMatch.Groups["exitCode"].Value);
        var pid = byte.Parse(resultMatch.Groups["pid"].Value);

        codes.Add((exitCode, pid));

        if (callback is null)
          continue;

        var cbSuccess = callback((output, exitCode, pid));
        if (!cbSuccess)
        {
          noneStopped = false;
          break;
        }
      }

      string? totalOutput = null;
      try
      {
        shellStream.Seek(0, SeekOrigin.Begin);

        using var shellStreamReader = new StreamReader(shellStream);
        totalOutput = await shellStreamReader.ReadToEndAsync(cancellationToken);
      }
      catch (Exception e)
      {
        analyticsReporter.LogError(e, "Failed in re-reading output");
      }

      var lastCode = codes.Last();
      var (commentary, success, result) = handler(shellStream, totalOutput, lastCode, noneStopped);

      return SshResult<T>.Completed(success: success, result, exitStatus: lastCode.exitCode, output: totalOutput) with { Commentary = commentary };
    }
    catch (Exception e)
    {
      return SshResult<T>.Threw(e);
    }
  }
}
