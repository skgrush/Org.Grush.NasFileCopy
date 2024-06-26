using System.Text;
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
  public async Task<LsblkDevice?> ListDevice(CancellationToken token, string label)
  {
    var result = await ListDevicesInternal(token, label);

    return result is not null
      ? LsblkDevice.Deserialize(result)
      : null;
  }

  /// <exception cref="T:System.Net.Sockets.SocketException">Socket connection to the SSH server or proxy server could not be established, or an error occurred while resolving the hostname.</exception>
  /// <exception cref="T:Renci.SshNet.Common.SshConnectionException">SSH session could not be established.</exception>
  /// <exception cref="T:Renci.SshNet.Common.SshAuthenticationException">Authentication of SSH session failed.</exception>
  public async Task<ListCommandAcceptableValues?> ListDevices(CancellationToken token)
  {
    var result = await ListDevicesInternal(token);

    return result is not null
      ? ListCommandAcceptableValues.Deserialize(result)
      : null;
  }

  private async Task<string?> ListDevicesInternal(CancellationToken token, string? label = null)
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
      if (label is null)
        return commandResult;
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
      $"sudo {ServerBinPath} copy --source-name '{sourceName}' --destination-device-label '{destinationDeviceLabel}'";

    var runner = client.CreateCommand(cmd);

    var asyncExe = runner.BeginExecute();

    using var stdoutReader = new StreamReader(runner.OutputStream, Encoding.UTF8, true, 1024, true);
    using var stderrReader = new StreamReader(runner.ExtendedOutputStream, Encoding.UTF8, true, 1024, true);

    var stderrTask = CheckOutputAndReportProgressAsync(runner, asyncExe, stderrReader, token);
    var stdoutTask = CheckOutputAndReportProgressAsync(runner, asyncExe, stdoutReader, token);

    await Task.WhenAll(stderrTask, stdoutTask);

    runner.EndExecute(asyncExe);

    if (runner.ExitStatus is 0)
    {
      Console.WriteLine();
      return true;
    }

    var exitMessage = ExitCodeToMessage((CopyCommandExitCodes)runner.ExitStatus);
    Console.WriteLine($"Error, exit status {runner.ExitStatus} ({exitMessage}): {runner.Error}");
    return false;
  }

  private static async Task CheckOutputAndReportProgressAsync(
    SshCommand sshCommand,
    IAsyncResult asyncResult,
    StreamReader streamReader,
    CancellationToken cancellationToken)
  {
    while (!asyncResult.IsCompleted || !streamReader.EndOfStream)
    {
      if (cancellationToken.IsCancellationRequested)
      {
        sshCommand.CancelAsync();
      }

      cancellationToken.ThrowIfCancellationRequested();

      var remaining = await streamReader.ReadToEndAsync(cancellationToken);

      if (!string.IsNullOrEmpty(remaining))
      {
        Console.Write(remaining);
      }

      // wait 10 ms
      await Task.Delay(10, cancellationToken);
    }
  }

  private string ExitCodeToMessage(CopyCommandExitCodes exitCode)
  {
    return !Enum.IsDefined(exitCode)
      ? "UNDEFINED" :
      exitCode.ToString();
  }
}