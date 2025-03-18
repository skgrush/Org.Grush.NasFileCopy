using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Org.Grush.NasFileCopy.Remote.Share;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Org.Grush.NasFileCopy.Remote.Ui.Extensions;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class TransfersPanel : ContentView
{
  private readonly IDataService _dataService;
  private readonly ITrueNasClient _trueNasClient;
  private readonly IPopUpService _popUpService;

  public ObservableCollection<ExistingRun> Runs { get; } = [];

  public TransfersPanel() : this(
    dataService: MauiProgram.ServiceProvider.GetRequiredService<IDataService>(),
    trueNasClient: MauiProgram.ServiceProvider.GetRequiredService<ITrueNasClient>(),
    popUpService: MauiProgram.ServiceProvider.GetRequiredService<IPopUpService>()
  )
  {
  }

  public TransfersPanel(
    IDataService dataService,
    ITrueNasClient trueNasClient,
    IPopUpService popUpService
  )
  {
    _dataService = dataService;
    _trueNasClient = trueNasClient;
    _popUpService = popUpService;

    InitializeComponent();
  }


  private async Task GetRunsAsync(CancellationToken cancellationToken)
  {
    var runsResult = await _trueNasClient.GetExistingRuns(cancellationToken);

    if (runsResult is { IsOk: true, Value: { IsEmpty: false } runs })
    {
      Runs.ReplaceIfDifferent(runs);
      return;

    }

    if (!runsResult.IsOk)
      await _popUpService.DisplayAlertAsync("Error", runsResult.Error, "OK");

    Runs.Clear();
  }
}