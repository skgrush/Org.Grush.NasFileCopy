using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share;

public class DangerousOperationException(string message, string paramName) : ArgumentException(message, paramName);

internal sealed class TrueNasSshClient(
  IAnalyticsReporter analyticsReporter
) : IAsyncDisposable
{
  private static readonly Regex DangerousShellQuoteChars = new("""[\\"\n]""");
  private static readonly Regex DangerousSingleQuoteChars = new("""[\\'\n]""");

  internal SshClient? SshClient { get; private set; }

  public bool IsDisposed { get; private set; }
  public bool IsConnected => SshClient?.IsConnected ?? false;

  public async Task ConnectAsync(
    ConnectionInfo sshCredentials,
    CancellationToken cancellationToken
  )
  {
    SshClient = new SshClient(sshCredentials);

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
        if (DangerousShellQuoteChars.IsMatch(filePath))
          throw new DangerousOperationException("contains dangerous characters.", filePath);
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
        if (DangerousShellQuoteChars.IsMatch(path))
          throw new DangerousOperationException("contains dangerous characters.", nameof(path));
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
  public async Task<SshResult<ImmutableArray<LsLine>>> LsVerboseAsync(string path, CancellationToken cancellationToken)
  {
    // TODO fix ~
    path = path.Replace("~", "$HOME");
    return await CallCommand<ImmutableArray<LsLine>>(
      preconditions: () =>
      {
        if (DangerousShellQuoteChars.IsMatch(path))
          throw new DangerousOperationException("contains dangerous characters.", nameof(path));
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
  }

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
    if (copyFrom.Contains('\''))
      throw new DangerousOperationException("cannot contain '", nameof(copyFrom));
    if (DangerousShellQuoteChars.IsMatch(destination))
      throw new DangerousOperationException("contains dangerous characters.", nameof(destination));
    if (SshClient?.IsConnected is not true)
      throw new InvalidOperationException("Connect first");

    return await RsyncLogReader.NewAsync(
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
    if (DangerousShellQuoteChars.IsMatch(path))
      throw new DangerousOperationException("contains dangerous characters.", name);
    // fix ~
    return path.Replace("~", "$HOME");
  }

  private string ProcessSingleQuotePath(string path, string name)
  {
    if (DangerousSingleQuoteChars.IsMatch(path))
      throw new DangerousOperationException("cannot contain ' or \\ or $", name);
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
