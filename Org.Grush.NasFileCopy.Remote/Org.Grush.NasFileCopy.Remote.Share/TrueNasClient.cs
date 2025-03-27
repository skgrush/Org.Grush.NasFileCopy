using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share;

public readonly record struct InitiateSyncResult(
  string RunId,
  string Mountpoint,
  bool CreatedUserMountpoint
);

internal sealed class TrueNasClient(
  TrueNasSshClient sshClient,
  TrueNasHttpClient httpClient
) : ITrueNasClient
{
  private readonly TrueNasSshClient _sshClient = sshClient;
  private readonly TrueNasHttpClient _httpClient = httpClient;

  private static readonly HashSet<string> CheckedDatasetFolders = ["/mnt/"];

  public bool IsSshConnected => _sshClient.IsConnected;
  public bool IsHttpConnected => _httpClient.IsHttpConnected;

  private Dictionary<string, RsyncLogReader> RsyncLogReaders { get; } = [];

  public async Task ConnectAsync(ConnectionInfo sshInfo, TrueNasHttpCredentials? httpCredentials, CancellationToken cancellationToken)
  {
    await _sshClient.ConnectAsync(sshInfo, cancellationToken);
    if (httpCredentials is not null)
      await _httpClient.ConnectAsync(httpCredentials, cancellationToken);
  }

  public async Task ReconnectAsync(CancellationToken cancellationToken)
  {
    if (!_sshClient.IsConnected)
      await _sshClient.ReconnectIfNeededAsync(cancellationToken);
  }

  public void Disconnect()
  {
    if (_sshClient.IsConnected)
      _sshClient.Disconnect();
  }

  public Task<UiResult<ImmutableArray<ExistingRun>>> GetExistingRuns(CancellationToken cancellationToken) =>
    UiResult.ExecuteAsync(async () => await RsyncLogReader.GetExistingRunsAsync(_sshClient, cancellationToken));

  /// <inheritdoc />
  public async Task<UiResult<ImmutableArray<SourceDataset>>> GetSourceDatasets(
    CancellationToken cancellationToken)
  {
    if (IsHttpConnected)
    {
      return await UiResult.ExecuteAsync(async () =>
      {
        var datasets = await _httpClient.GetDatasetsAsync(cancellationToken);

        return datasets
          .SelectMany(d => d.DepthfirstRecurse())
          .Select(d => new SourceDataset(d.Name, d.Mountpoint))
          .ToImmutableArray();
      });
    }

    var allMountsResult = await _sshClient.MountListAsync(cancellationToken);

    if (!allMountsResult.Success)
      return allMountsResult.ToUiError<ImmutableArray<SourceDataset>>();

    var allowedDatasets = allMountsResult.Result
      .Where(m => CheckedDatasetFolders.Any(f => m.MountPoint.StartsWith(f)));

    return allowedDatasets
      .Select(d => new SourceDataset(d.Device, d.MountPoint))
      .ToImmutableArray();
  }

  public async Task<UiResult<ImmutableArray<LsblkDevice>>> GetDestinationDevices(bool onlyHotpluggable,
    CancellationToken cancellationToken, bool onlyPartitions)
  {
    var lsblkResult = await _sshClient.LsblkAsync(cancellationToken);

    if (!lsblkResult.Success)
      return lsblkResult.ToUiError<ImmutableArray<LsblkDevice>>();

    var destinations = lsblkResult.Result.Blockdevices
      .SelectMany(d => d.DepthfirstRecurse())
      .ToImmutableArray();
    if (onlyPartitions)
      destinations = [..destinations.Where(d => d.Type is "part")];
    if (onlyHotpluggable)
      destinations = [..destinations.Where(d => d.Hotplug)];

    return destinations;
  }

  public IAsyncEnumerable<RsyncLogState>? ListenToLocallyConnectedLog(string runId, Func<string, Task> errorLogger, CancellationToken cancellationToken)
    => RsyncLogReaders.GetValueOrDefault(runId)?.ListenAsync(errorLogger, cancellationToken);

  /// <inheritdoc />
  public Task<UiResult<bool>> LoadLogReaderAsync(string runId, bool overwriteAndDisposeExisting, CancellationToken cancellationToken)
    => UiResult.ExecuteAsync(async () =>
    {
      if (RsyncLogReaders.TryGetValue(runId, out var existing))
      {
        if (!overwriteAndDisposeExisting)
          return false;

        await ((IAsyncDisposable)existing).DisposeAsync();
      }

      var reader = await RsyncLogReader.PickUpExistingAsync(_sshClient, runId, cancellationToken);

      RsyncLogReaders[reader.RunId] = reader;

      return true;
    });

  private static readonly Regex MountpointDangerChars = new("""[/\\"']|\.\.""");

  public async Task<UiResult<InitiateSyncResult>> InitiateSyncFromDataSourceToDevice(
    Func<Task<string?>> promptPassword,
    SourceDataset srcDataset,
    LsblkDevice destinationDevice,
    string? destinationFolder,
    CancellationToken cancellationToken
  )
  {
    try
    {
      string copyFrom = srcDataset.Mountpoint;

      bool createdUserMountpoint = false;
      string destinationMountpoint;
      if (destinationDevice.Mountpoint is null or "")
      {
        createdUserMountpoint = true;
        var sudoResult = await _sshClient.SudoElevateAsync(promptPassword, cancellationToken);

        if (!sudoResult.Result)
          return sudoResult.ToUiError<InitiateSyncResult>();

        string userMountBaseName =
          destinationDevice.Label is null
            ? destinationDevice.Partuuid ?? throw new InvalidOperationException("LsblkDevice has neither partuuid nor label")
            : MountpointDangerChars.Replace(destinationDevice.Label, "");
        destinationMountpoint = $"$HOME/mnt/{userMountBaseName}";

        using (var mkdirCmd = _sshClient.SshClient!.CreateCommand($"mkdir -p \"{destinationMountpoint}\""))
        {
          await mkdirCmd.ExecuteAsync(cancellationToken);
          if (mkdirCmd.ExitStatus is not 0)
            return UiResult<InitiateSyncResult>.Err(mkdirCmd.Error);
        }

        var mntResult = await _sshClient.MountDeviceAsync(
          destinationDevice.Path,
          destinationMountpoint,
          cancellationToken
        );

        if (!mntResult.Success)
          return mntResult.ToUiError<InitiateSyncResult>();
      }
      else
      {
        destinationMountpoint = destinationDevice.Mountpoint;
      }

      string destination;
      if (destinationFolder is null or "")
        destination = destinationMountpoint;
      else if (destinationMountpoint.EndsWith('/'))
        destination = destinationMountpoint + destinationFolder;
      else
        destination = destinationMountpoint + '/' + destinationFolder;

      RsyncLogReader reader = await _sshClient.Rsync(
        promptPassword: promptPassword,
        copyFrom: copyFrom,
        destination: destination,
        cancellationToken: cancellationToken
      );

      RsyncLogReaders[reader.RunId] = reader;

      return new InitiateSyncResult(reader.RunId, destinationMountpoint, createdUserMountpoint);
    }
    catch (Exception ex)
    {
      return UiResult<InitiateSyncResult>.Err(ex.ToString());
    }
  }

  private Task<UiResult<(string runId, object?)>> InitiateSyncFromFolderToDevice(Func<Task<string?>> promptPassword, string copyFrom, string destination, CancellationToken cancellationToken)
    => UiResult.ExecuteAsync(async () =>
    {
      RsyncLogReader reader = await _sshClient.Rsync(
        promptPassword: promptPassword,
        copyFrom: copyFrom,
        destination: destination,
        cancellationToken: cancellationToken
      );

      RsyncLogReaders[reader.RunId] = reader;

      return (reader.RunId, (object?)null);
    });

  async ValueTask IAsyncDisposable.DisposeAsync()
  {
    await ((IAsyncDisposable)_sshClient).DisposeAsync();
    await ((IAsyncDisposable)_httpClient).DisposeAsync();
    await Task.WhenAll(RsyncLogReaders.Values.Cast<IAsyncDisposable>().Select(async r => await r.DisposeAsync()));
  }
}