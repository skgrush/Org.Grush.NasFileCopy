using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share;

public class DangerousOperationException(string message, string paramName) : ArgumentException(message, paramName);


public record MountListLine(
  string Device,
  string MountPoint,
  ImmutableHashSet<string> Flags
)
{
  public static readonly Regex Re = new("""
    ^(?<device>.*?)
    \ on\ (?<path>.*?)
    (\ type\ (?<type>.*))? # some IMPLs list type inline, some have it as the first flag
    \ \((?<flags>.*)\) # some IMPLs separate flags by comma and space, some omit space
    $
    """, RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace);

  public static IEnumerable<MountListLine> ReadLines(string lines)
    => Re.Matches(lines)
      .Select(v =>
        new MountListLine(
          Device: v.Groups["device"].Value,
          MountPoint: v.Groups["path"].Value,
          Flags: ReadFlags(v.Groups["flags"].Value)
        )
      );

  private static ImmutableHashSet<string> ReadFlags(string flags)
    => flags.Split(',', StringSplitOptions.RemoveEmptyEntries)
      .Select(v => v.Trim())
      .ToImmutableHashSet();
}

public sealed class TrueNasSshClient(
  ConnectionInfo sshCredentials,
  IAnalyticsReporter analyticsReporter
) : IAsyncDisposable
{
  private readonly SshClient _sshClient = new(sshCredentials);
  private readonly Regex _dangerousShellQuoteChars = new("""[\\"\n]""");
  private readonly Regex _dangerousSingleQuoteChars = new("""[\\'\n]""");

  public async Task ConnectAsync(CancellationToken cancellationToken)
  {
    await _sshClient.ConnectAsync(cancellationToken);
  }

  public async Task<SshResult<bool>> UnmountAsync(string devPath, CancellationToken cancellationToken) =>
    await CallCommand<bool>(
      preconditions: null,
      getCommand: () => $"sudo umount '{ProcessSingleQuotePath(devPath, nameof(devPath))}'",
      handler: command =>
      {
        if (command.ExitStatus is 0)
          return (null, true, null);

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

  private string ProcessDoubleQuotePath(string path, string name)
  {
    if (_dangerousShellQuoteChars.IsMatch(path))
      throw new DangerousOperationException("contains dangerous characters.", name);
    return path.Replace("~", "$HOME");
  }

  private string ProcessSingleQuotePath(string path, string name)
  {
    if (_dangerousSingleQuoteChars.IsMatch(path))
      throw new DangerousOperationException("cannot contain ' or \\ or $", name);
    return path;
  }

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

  public async Task<SshResult<ImmutableArray<string>>> LsAsync(string path, CancellationToken cancellationToken)
  {
    path = path.Replace("~", "$HOME");
    return await CallCommand<ImmutableArray<string>>(
      preconditions: () =>
      {
        if (_dangerousShellQuoteChars.IsMatch(path))
          throw new DangerousOperationException("contains dangerous characters.", nameof(path));
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
  }

  public RsyncReader Rsync(
    string copyFrom,
    string destination,
    CancellationToken cancellationToken
  )
  {
    if (copyFrom.Contains('\''))
      throw new DangerousOperationException("cannot contain '", nameof(copyFrom));
    if (_dangerousShellQuoteChars.IsMatch(destination))
      throw new DangerousOperationException("contains dangerous characters.", nameof(destination));

    return new RsyncReader(
      _sshClient,
      $"sudo rsync --verbose --archive --no-o --no-g --stats -P '{copyFrom}' \"{destination}\"",
      cancellationToken: cancellationToken
    );
  }

  public async ValueTask DisposeAsync()
  {
    _sshClient.Dispose();
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
      preconditions?.Invoke();

      using var command = _sshClient.CreateCommand(getCommand());
      await using var _ = cancellationToken.Register(c => (c as SshCommand)?.CancelAsync(),
        useSynchronizationContext: true, state: command);

      await Task.Factory.FromAsync(
        asyncResult: command.BeginExecute(),
        endMethod: command.EndExecute
      );

      (string? commentary, bool succeeded, T? result) = handler(command);

      return SshResult<T>.Completed(success: succeeded, result, command) with { Commentary = commentary };
    }
    catch (Exception e)
    {
      return SshResult<T>.Threw(e);
    }
  }
}
