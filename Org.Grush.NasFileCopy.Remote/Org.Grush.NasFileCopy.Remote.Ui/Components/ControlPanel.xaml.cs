using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class ControlPanel : ContentView
{
  private readonly IDataService _dataService;

  public IReadOnlyCollection<SourceDataset> SourceDatasets => _dataService.SourceDatasets;
  public IReadOnlyCollection<LsblkDevice> DestinationDevices => _dataService.DestinationDevices;

  public ControlPanel() : this(
    dataService: MauiProgram.ServiceProvider.GetRequiredService<IDataService>()
  )
  {
  }

  public ControlPanel(IDataService dataService)
  {
    _dataService = dataService;

    InitializeComponent();
    BindingContext = this;
  }

  private void SourceDatasetPicker_OnSelectedIndexChanged(object? sender, EventArgs e)
  {
    var selected = (SourceDataset?)SourceDatasetPicker.SelectedItem;

    _dataService.PickSource(selected);
  }

  private void DestinationPicker_OnSelectedIndexChanged(object? sender, EventArgs e)
  {
    var selected = (LsblkDevice?)DestinationPicker.SelectedItem;

    _dataService.PickDestination(selected);
  }
}