using System.Collections.Immutable;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share;

public interface ITrueNasClient : IAsyncDisposable
{
  bool IsSshConnected { get; }
  bool IsHttpConnected { get; }
  Task ConnectAsync(ConnectionInfo sshInfo, TrueNasHttpCredentials? httpCredentials, CancellationToken cancellationToken);
  Task ReconnectAsync(CancellationToken cancellationToken);
  void Disconnect();
  Task<UiResult<ImmutableArray<ExistingRun>>> GetExistingRuns(CancellationToken cancellationToken);

  /// <summary>
  /// Find mountable datasets mapped by human-readable name to mountpoint.
  ///
  /// If the API client is connected, we'll retrieve official TrueNas datasets.
  /// If not, we will only return mountpoints that that start with <see cref="TrueNasClient.CheckedDatasetFolders"/>.
  /// </summary>
  Task<UiResult<ImmutableArray<(string Name, string Mountpoint)>>> GetSourceDatasets(CancellationToken cancellationToken);

  Task<UiResult<ImmutableArray<LsblkDevice>>> GetDestinationDevices(bool onlyHotpluggable, CancellationToken cancellationToken);
  IAsyncEnumerable<RsyncLogState>? ListenToLocallyConnectedLog(string runId, Func<string, Task> errorLogger, CancellationToken cancellationToken);

  /// <summary>
  /// Load an already run/ning log-reader by its runId.
  /// </summary>
  /// <returns>if <paramref name="overwriteAndDisposeExisting"/> is true, will return <c>false</c> if we found it locally and didn't load remotely.</returns>
  Task<UiResult<bool>> LoadLogReaderAsync(string runId, bool overwriteAndDisposeExisting, CancellationToken cancellationToken);

  Task<UiResult<(string runId, object?)>> InitiateSyncFromFolderToDevice(string password, string copyFrom, string destination, CancellationToken cancellationToken);
}