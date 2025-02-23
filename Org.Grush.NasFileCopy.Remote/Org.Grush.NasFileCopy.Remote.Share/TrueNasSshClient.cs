using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share;

public class DangerousOperationException(string Message) : InvalidOperationException(Message);

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

  public async Task MountAsync(string devPath, string destinationPath, CancellationToken cancellationToken)
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

    if (command.ExitStatus is not 0)
      throw new Exception($"mount exited with code {command.ExitStatus}");
  }

  public async Task<ImmutableArray<string>> LsAsync(string path, CancellationToken cancellationToken)
  {
    if (_dangerousShellQuoteChars.IsMatch(path))
      throw new DangerousOperationException("path contains dangerous characters.");
    path = path.Replace("~", "$HOME");

    using var command = _sshClient.RunCommand($"ls -1 \"{path}\" ");

    var result = await Task.Factory.FromAsync(
      asyncResult: command.BeginExecute(),
      endMethod: command.EndExecute
    );

    if (command.ExitStatus is not 0)
      throw new InvalidOperationException($"ls shouldn't throw? It exited {command.ExitStatus}");

    return [
      ..result
        .TrimEnd('\n')
        .Split('\n')
    ];
  }

  public async ValueTask DisposeAsync()
  {
    _sshClient.Dispose();
  }
}