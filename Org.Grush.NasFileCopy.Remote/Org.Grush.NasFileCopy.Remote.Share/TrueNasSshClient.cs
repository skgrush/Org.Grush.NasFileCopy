using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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

  public async Task<SshResult<bool>> UnmountAsync(string devPath, CancellationToken cancellationToken) =>
    await CallCommand<bool>(
      preconditions: null,
      getCommand: () => $"sudo umount '{ProcessSingleQuotePath(devPath, nameof(devPath))}'",
      handler: command =>
      {
        if (command.ExitStatus is 0)
          return (null, true, true);

        analyticsReporter.LogError("Unmount device exited with {status}", command.ExitStatus);
        return ($"Error: unmount device exit status {command.ExitStatus}", false, null);
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

  public async Task<SshResult<bool>> MountDeviceAsync(string devPath, string destinationPath, CancellationToken cancellationToken) =>
    await CallCommand<bool>(
      preconditions: null,
      getCommand: () => $"mount -rw '{ProcessSingleQuotePath(devPath, nameof(devPath))}' \"{ProcessDoubleQuotePath(destinationPath, nameof(destinationPath))}\"",
      handler: command =>
      {
        if (command.ExitStatus is 0)
          return (null, true, true);

        analyticsReporter.LogError("Mount device exited with {status}", command.ExitStatus);
        return ($"Error: mount device exit status {command.ExitStatus}", false, null);
      },
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
        if (PathValidation.IsInvalidatePathEntry(path, out var errors, doubleQuotable: true))
          throw new DangerousOperationException("contains dangerous characters.", errors, nameof(path));
      },
      getCommand: () => $"ls -1 \"{path.Replace("~", "$HOME")}\" ",
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
    string password,
    string copyFrom,
    string destination,
    CancellationToken cancellationToken
  )
  {
    if (PathValidation.IsInvalidatePathEntry(copyFrom, out var sqErrors, singleQuotable: true))
      throw new DangerousOperationException("contains dangerous characters", sqErrors, nameof(copyFrom));
    if (PathValidation.IsInvalidatePathEntry(destination, out var dqErrors, doubleQuotable: true))
      throw new DangerousOperationException("contains dangerous characters", dqErrors, nameof(destination));
    if (SshClient?.IsConnected is not true)
      throw new InvalidOperationException("Connect first");

    return await RsyncLogReader.CreateAndStartNewAsync(
      password: password,
      client: this,
      copyFrom: $"'{copyFrom}'",
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
}
