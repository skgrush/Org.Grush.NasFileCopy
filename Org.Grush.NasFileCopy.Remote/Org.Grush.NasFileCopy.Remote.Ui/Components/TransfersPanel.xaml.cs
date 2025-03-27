using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class TransfersPanel : ContentView
{
  private readonly IDataService _dataService;

  public IReadOnlyCollection<ExistingRun> Runs => _dataService.Runs;

  public TransfersPanel() : this(
    dataService: MauiProgram.ServiceProvider.GetRequiredService<IDataService>()
  )
  {
  }

  public TransfersPanel(
    IDataService dataService
  )
  {
    _dataService = dataService;

    InitializeComponent();
  }


  private async Task GetRunsAsync(CancellationToken cancellationToken)
  {
    await _dataService.GetRunsAsync(cancellationToken);
  }
}