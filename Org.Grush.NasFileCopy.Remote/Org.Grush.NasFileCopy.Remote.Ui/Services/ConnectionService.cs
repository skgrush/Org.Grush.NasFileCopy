using Org.Grush.NasFileCopy.Remote.Share;
using Renci.SshNet;

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
    IStorageService storageService
  )
  {
    _trueNasClient = trueNasClient;
    _storageService = storageService;
    storageService.ConfigChanged += StorageConfigChanged;
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
    }).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
  }

  private void StorageConfigChanged(object? sender, (StorageConfig?, StorageConfig) tuple)
  {
    var (previousStorageConfig, newStorageConfig) = tuple;

    var newConnectionConfig = Config;
    var oldConnectionConfig = Config;

    try
    {
      if (previousStorageConfig?.PathToSshKey != newStorageConfig.PathToSshKey)
      {
        var privateKeyFile = newStorageConfig.PathToSshKey is null
          ? null
          : new PrivateKeyFile(newStorageConfig.PathToSshKey);

        // only rebuild the connectionInfo if there already was one AND we have ALL the necessary stuff
        var connectionInfo = (
          privateKeyFile is not null &&
            oldConnectionConfig is { ConnectionInfo: not null } &&
            newStorageConfig is { Username: {} username, Hostname: {} hostname }
          )
          ? new PrivateKeyConnectionInfo(host: hostname, username: username, privateKeyFile)
          : null;

        newConnectionConfig = newConnectionConfig with
        {
          IsConnected = false,
          ConnectionInfo = connectionInfo,
          PrivateKeyFile = privateKeyFile,
        };
      }
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