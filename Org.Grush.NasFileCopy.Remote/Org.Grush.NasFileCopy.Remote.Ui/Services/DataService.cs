using System.Collections.ObjectModel;
using Org.Grush.NasFileCopy.Remote.Share;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Org.Grush.NasFileCopy.Remote.Ui.Extensions;

namespace Org.Grush.NasFileCopy.Remote.Ui.Services;

public interface IDataService
{
  IReadOnlyCollection<SourceDataset> SourceDatasets { get; }
  IReadOnlyCollection<LsblkDevice> DestinationDevices { get; }
  IReadOnlyCollection<ExistingRun> Runs { get; }
  void CancelRefresh();
  Task RefreshAsync();

  void PickSource(SourceDataset? source);
  void PickDestination(LsblkDevice? destination);
  Task InitiateSync(SourceDataset src, LsblkDevice dest, string? destPath);
  Task GetRunsAsync(CancellationToken cancellationToken);
}

internal class DataService : IDataService
{
  private readonly ITrueNasClient _client;
  private readonly IConnectionService _connectionService;
  private readonly IPopUpService _popUpService;

  private readonly ObservableCollection<SourceDataset> _sourceDatasets = [];
  private readonly ObservableCollection<LsblkDevice> _destinationDevices = [];
  private readonly ObservableCollection<ExistingRun> _runs = [];

  private SourceDataset? PickedSource { get; set; }
  private LsblkDevice? PickedDestination { get; set; }

  public IReadOnlyCollection<ExistingRun> Runs => _runs;
  public IReadOnlyCollection<SourceDataset> SourceDatasets => _sourceDatasets;
  public IReadOnlyCollection<LsblkDevice> DestinationDevices => _destinationDevices;

  private CancellationTokenSource ConnectionChangeCanceller { get; set; } = new();

  public DataService(
    ITrueNasClient client,
    IConnectionService connectionService,
    IPopUpService popUpService
  )
  {
    _client = client;
    _connectionService = connectionService;
    _popUpService = popUpService;


    _connectionService.ConnectionChanged += OnConnectionChanged;
  }

  public void PickSource(SourceDataset? source)
  {
    if (source is null || _sourceDatasets.Contains(source.Value))
      PickedSource = source;
  }

  public void PickDestination(LsblkDevice? destination)
  {
    if (destination is null || _destinationDevices.Contains(destination.Value))
      PickedDestination = destination;
  }

  public async Task GetRunsAsync(CancellationToken cancellationToken)
  {
    var runsResult = await _client.GetExistingRuns(cancellationToken);

    if (runsResult is { IsOk: true, Value: { IsEmpty: false } runs })
    {
      _runs.ReplaceIfDifferent(runs);
      return;
    }

    if (!runsResult.IsOk)
      await _popUpService.DisplayAlertAsync("Error", runsResult.Error, "OK");

    _runs.Clear();
  }

  public async Task InitiateSync(SourceDataset src, LsblkDevice dest, string? destPath)
  {
    var token = ConnectionChangeCanceller.Token;

    var result = await _client.InitiateSyncFromDataSourceToDevice(
      promptPassword: PromptPassword,
      srcDataset: src,
      destinationDevice: dest,
      destinationFolder: destPath,
      token
    );

    if (!result.IsOk)
    {
      await _popUpService.DisplayAlertAsync("Error while initiating", result.Error, cancel: "Close");
      return;
    }

    var initResult = result.Value;

    var runTask = GetRunsAsync(token);
    if (initResult.CreatedUserMountpoint)
      await Task.WhenAll(RefreshAsync(), runTask);
    else
      await runTask;

    return;

    Task<string?> PromptPassword() => _popUpService.DisplayPasswordPromptAsync("Enter password", "Enter password for sudo elevation", "OK", "Cancel");
  }

  public void CancelRefresh()
  {
    ConnectionChangeCanceller.Cancel();
    ConnectionChangeCanceller.Dispose();
    ConnectionChangeCanceller = new CancellationTokenSource();
  }

  public Task RefreshAsync() => UpdatePickers();

  private void OnConnectionChanged(object? sender, ConnectionConfig e)
  {
    _ = UpdatePickers().ConfigureAwait(true);
  }

  private async Task UpdatePickers()
  {
    CancelRefresh();

    if (!_client.IsSshConnected)
    {
      ClearData();
      return;
    }

    var token = ConnectionChangeCanceller.Token;
    var datasetTask = _client.GetSourceDatasets(token).ConfigureAwait(true);
    var destinationsTask = _client.GetDestinationDevices(true, token).ConfigureAwait(true);

    await datasetTask;
    await destinationsTask;

    var datasetResult = datasetTask.GetAwaiter().GetResult();
    var destinationResult = destinationsTask.GetAwaiter().GetResult();

    if (!datasetResult.IsOk || !destinationResult.IsOk)
    {
      ClearData();
      return;
    }

    _sourceDatasets.ReplaceIfDifferent(datasetResult.Value);
    _destinationDevices.ReplaceIfDifferent(destinationResult.Value);

    if (PickedSource.HasValue && !_sourceDatasets.Contains(PickedSource.Value))
      PickedSource = null;
    if (PickedDestination.HasValue && !_destinationDevices.Contains(PickedDestination.Value))
      PickedDestination = null;
  }

  private void ClearData()
  {
    _sourceDatasets.Clear();
    _destinationDevices.Clear();
    PickedSource = null;
    PickedDestination = null;
  }
}