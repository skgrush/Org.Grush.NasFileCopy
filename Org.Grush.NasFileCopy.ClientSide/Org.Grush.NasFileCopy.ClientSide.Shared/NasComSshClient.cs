using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Org.Grush.NasFileCopy.Structures;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.ClientSide.Shared;

public record struct SshCredentials(
  string Host,
  string Username,
  [property: MemberNotNullWhen(true, nameof(Password))]
  [property: MemberNotNullWhen(false, nameof(Key))]
  bool UsesPassword,
  string? Password,
  string? Key
);

public class NasComSshClient
{
  private readonly ConnectionInfo _sshCredentials;
  private readonly string _serverBinDirectory;

  public string ServerBinPath => Path.Join(_serverBinDirectory, "Org.Grush.NasFileCopy.ServerSide");

  public NasComSshClient(ConnectionInfo sshCredentials, string serverBinDirectory)
  {
    _sshCredentials = sshCredentials;
    _serverBinDirectory = serverBinDirectory;

    if (!new Regex("^[a-z0-9/]+$", RegexOptions.IgnoreCase).IsMatch(serverBinDirectory))
      throw new InvalidOperationException($"{nameof(serverBinDirectory)} is not a safe path");
  }

  /// <exception cref="T:System.Net.Sockets.SocketException">Socket connection to the SSH server or proxy server could not be established, or an error occurred while resolving the hostname.</exception>
  /// <exception cref="T:Renci.SshNet.Common.SshConnectionException">SSH session could not be established.</exception>
  /// <exception cref="T:Renci.SshNet.Common.SshAuthenticationException">Authentication of SSH session failed.</exception>
  public async Task<LsblkOutput?> ListDevices(CancellationToken token, string? label = null)
  {
    if (label is not null && label.Contains('\''))
      throw new InvalidOperationException($"{nameof(label)} cannot contain an apostrophe");

    using var client = new SshClient(_sshCredentials);

    await client.ConnectAsync(token);

    var cmd =
      label is null
        ?$"{ServerBinPath} list"
        : $"{ServerBinPath} list --label '{label}'";

    var runner = client.RunCommand($"{ServerBinPath} '{label}'");

    var commandResult = await Task.Factory.FromAsync(runner.BeginExecute(), runner.EndExecute).ConfigureAwait(false);

    if (runner.ExitStatus is 0)
    {
      return LsblkOutput.Deserialize(commandResult);
    }

    Console.WriteLine($"Error, exit status {runner.ExitStatus}: {runner.Error}");
    return null;
  }

}