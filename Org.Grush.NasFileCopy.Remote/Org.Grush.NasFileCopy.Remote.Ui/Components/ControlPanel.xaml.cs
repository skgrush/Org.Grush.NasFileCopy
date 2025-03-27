using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Org.Grush.NasFileCopy.Remote.Share.Validation;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class ControlPanel : ContentView
{
  private readonly IDataService _dataService;
  private readonly IPopUpService _popUpService;

  public IReadOnlyCollection<SourceDataset> SourceDatasets => _dataService.SourceDatasets;
  public IReadOnlyCollection<LsblkDevice> DestinationDevices => _dataService.DestinationDevices;

  public ControlPanel() : this(
    dataService: MauiProgram.ServiceProvider.GetRequiredService<IDataService>(),
    popUpService: MauiProgram.ServiceProvider.GetRequiredService<IPopUpService>()
  )
  {
  }

  public ControlPanel(IDataService dataService, IPopUpService popUpService)
  {
    _dataService = dataService;
    _popUpService = popUpService;

    InitializeComponent();
    BindingContext = this;
  }

  public (SourceDataset src, LsblkDevice dest, string? destPath)? Validate()
  {
    var src = SourceDatasetPicker.SelectedItem as SourceDataset?;
    var dest = DestinationPicker.SelectedItem as LsblkDevice?;
    string? destPath = DestinationPathEntry.Text;
    if (destPath is "")
      destPath = null;

    List<string> allErrors = [];

    if (destPath is not null && PathValidation.IsInvalidatePathEntry(destPath, out var errors, doubleQuotable: true, noVariables: true, notAbsolute: true))
      allErrors.AddRange(errors.Select(e => $"Destination path: {e}"));

    if (src is null || !SourceDatasets.Contains(src.Value))
      allErrors.Add("Source dataset is not valid");

    if (dest is null || !DestinationDevices.Contains(dest.Value))
      allErrors.Add("Destination dataset is not valid");

    if (allErrors.Count is 0)
      return (src!.Value, dest!.Value, destPath);

    _popUpService.DisplayAlertAsync("Form errors", string.Join("\n", allErrors), cancel: "OK");
    return null;
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


  private void SubmitBtn_OnClicked(object? sender, EventArgs e)
  {
    if (Validate() is not (SourceDataset src, LsblkDevice dest, var destPath))
      return;

    _dataService.InitiateSync(src, dest, destPath);
  }
}