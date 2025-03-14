using System.Text;
using Org.Grush.NasFileCopy.Remote.Share;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Org.Grush.NasFileCopy.Remote.Ui.Services;

public record ConnectionConfig(
  bool IsConnected,
  ConnectionInfo? ConnectionInfo,
  string? Hostname,
  string? Username,
  PrivateKeyFile? PrivateKeyFile
);

internal class ConnectionService
{
  private readonly ITrueNasClient _trueNasClient;
  private readonly IStorageService _storageService;
  private readonly IPopUpService _popUpService;


  public ConnectionConfig Config { get; private set; } = new(
    IsConnected: false,
    ConnectionInfo: null,
    Hostname: null,
    Username: null,
    PrivateKeyFile: null
  );

  public event EventHandler<ConnectionConfig>? ConnectionChanged;

  public ConnectionService(
    ITrueNasClient trueNasClient,
    IStorageService storageService,
    IPopUpService popUpService
  )
  {
    _trueNasClient = trueNasClient;
    _storageService = storageService;
    _popUpService = popUpService;

    _storageService.ConfigChanged += StorageConfigChanged;
  }

  public void ConnectAsync()
  {
    if (Config.IsConnected)
    {
      Config = Config with { IsConnected = false };
      ConnectionChanged?.Invoke(this, Config);
      return;
    }

    if (Config.ConnectionInfo is not { } connectionInfo)
      return;

    Task.Run(async () =>
    {
      try
      {
        await _trueNasClient.ConnectAsync(connectionInfo, null, CancellationToken.None);
        Config = Config with { IsConnected = true };
        ConnectionChanged?.Invoke(this, Config);
      }
      catch
      {
        // TODO: error handling
      }
    }).ConfigureAwait(true);
  }

  private async void StorageConfigChanged(object? sender, (StorageConfig?, StorageConfig) tuple)
  {
    var (previousStorageConfig, newStorageConfig) = tuple;

    await Handle(previousStorageConfig, newStorageConfig);
  }

  private async Task Handle(StorageConfig? previousStorageConfig, StorageConfig newStorageConfig)
  {
    var newConnectionConfig = Config;
    var oldConnectionConfig = Config;

    try
    {
      if (previousStorageConfig != newStorageConfig)
      {
        PrivateKeyFile? privateKeyFile = null;
        if (newStorageConfig.PrivateKey is not null)
        {
          using var stream = new MemoryStream(Encoding.UTF8.GetBytes(newStorageConfig.PrivateKey));

          string? passphrase = null;
          if (newStorageConfig.PrivateKeyHasPassphrase)
          {
            passphrase = await _popUpService.DisplayPromptAsync(
              title: "Private key passphrase",
              message: "Enter passphrase for private key"
            );
          }

          privateKeyFile = new PrivateKeyFile(stream, passphrase);
        }

        // only rebuild the connectionInfo if we have ALL the necessary stuff
        var connectionInfo = (
          privateKeyFile is not null &&
          // oldConnectionConfig is { ConnectionInfo: not null } &&
          newStorageConfig is { Username: { } username, Hostname: { } hostname }
        )
          ? new PrivateKeyConnectionInfo(host: hostname, username: username, privateKeyFile)
          : null;

        newConnectionConfig = newConnectionConfig with
        {
          IsConnected = false,
          ConnectionInfo = connectionInfo,
          PrivateKeyFile = privateKeyFile,
          Hostname = newStorageConfig.Hostname,
          Username = newStorageConfig.Username,
        };
      }
    }
    catch (SshException /*ex*/)
    {
      _storageService.WriteConfig(old => old with { PrivateKey = null });
      // TODO: error handling, private key invalid
    }
    catch (Exception ex)
    {
      Console.WriteLine(ex);
      // TODO: error handling
    }
    finally
    {
      if (oldConnectionConfig != newConnectionConfig)
      {
        Config = newConnectionConfig;

        if (!Config.IsConnected && _trueNasClient.IsSshConnected)
          _trueNasClient.Disconnect();

        ConnectionChanged?.Invoke(this, newConnectionConfig);
      }
    }
  }
}