using System.Text.RegularExpressions;
using Org.Grush.NasFileCopy.Structures;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.ClientSide.Shared;

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

    var runner = client.RunCommand(cmd);

    var commandResult = await Task.Factory.FromAsync(runner.BeginExecute(), runner.EndExecute).ConfigureAwait(false);

    if (runner.ExitStatus is 0)
    {
      return LsblkOutput.Deserialize(commandResult);
    }

    Console.WriteLine($"Error, exit status {runner.ExitStatus}: {runner.Error}");
    return null;
  }

  /// <exception cref="T:System.Net.Sockets.SocketException">Socket connection to the SSH server or proxy server could not be established, or an error occurred while resolving the hostname.</exception>
  /// <exception cref="T:Renci.SshNet.Common.SshConnectionException">SSH session could not be established.</exception>
  /// <exception cref="T:Renci.SshNet.Common.SshAuthenticationException">Authentication of SSH session failed.</exception>
  public async Task<bool> Copy(CancellationToken token, string sourceName, string destinationDeviceLabel)
  {
    if (sourceName.Contains('\''))
      throw new InvalidOperationException($"{nameof(sourceName)} cannot contain an apostrophe");
    if (destinationDeviceLabel.Contains('\''))
      throw new InvalidOperationException($"{nameof(destinationDeviceLabel)} cannot contain an apostrophe");

    using var client = new SshClient(_sshCredentials);

    await client.ConnectAsync(token);

    var cmd =
      $"sudo -E {ServerBinPath} copy --source-name '{sourceName}' --destination-device-label '{destinationDeviceLabel}'";

    var runner = client.RunCommand(cmd);

    // await using var sw = new StreamWriter(Console.OpenStandardOutput());
    // sw.AutoFlush = true;

    // runner.OutputStream..SetOut(sw);

    await Task.Factory.FromAsync(runner.BeginExecute(), runner.EndExecute).ConfigureAwait(false);

    if (runner.ExitStatus is 0)
    {
      return true;
    }

    Console.WriteLine($"Error, exit status {runner.ExitStatus}: {runner.Error}");
    return false;
  }
}