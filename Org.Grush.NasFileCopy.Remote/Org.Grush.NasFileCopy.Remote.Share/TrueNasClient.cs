using System.Collections.Immutable;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share;

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

  public Task<UiResult<ImmutableArray<ExistingRun>>> GetExistingRuns(CancellationToken cancellationToken) =>
    UiResult.ExecuteAsync(async () => await RsyncLogReader.GetExistingRunsAsync(_sshClient, cancellationToken));

  /// <inheritdoc />
  public async Task<UiResult<ImmutableArray<(string Name, string Mountpoint)>>> GetSourceDatasets(CancellationToken cancellationToken)
  {
    if (IsHttpConnected)
    {
      return await UiResult.ExecuteAsync(async () =>
      {
        var datasets = await _httpClient.GetDatasetsAsync(cancellationToken);

        return datasets
          .SelectMany(d => d.DepthfirstRecurse())
          .Select(d => (d.Name, d.Mountpoint))
          .ToImmutableArray();
      });
    }

    var allMountsResult = await _sshClient.MountListAsync(cancellationToken);

    if (!allMountsResult.Success)
      return allMountsResult.ToUiError<ImmutableArray<(string Name, string Mountpoint)>>();

    var allowedDatasets = allMountsResult.Result
      .Where(m => CheckedDatasetFolders.Any(f => m.MountPoint.StartsWith(f)));

    return allowedDatasets
      .Select(d => (d.Device, d.MountPoint))
      .ToImmutableArray();
  }

  public async Task<UiResult<ImmutableArray<LsblkDevice>>> GetDestinationDevices(bool onlyHotpluggable, CancellationToken cancellationToken)
  {
    var lsblkResult = await _sshClient.LsblkAsync(cancellationToken);

    if (!lsblkResult.Success)
      return lsblkResult.ToUiError<ImmutableArray<LsblkDevice>>();

    var destinations = lsblkResult.Result.Blockdevices;
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
  public Task<UiResult<(string runId, object?)>> InitiateSyncFromFolderToDevice(string password, string copyFrom, string destination, CancellationToken cancellationToken)
    => UiResult.ExecuteAsync(async () =>
    {
      RsyncLogReader reader = await _sshClient.Rsync(
        password: password,
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