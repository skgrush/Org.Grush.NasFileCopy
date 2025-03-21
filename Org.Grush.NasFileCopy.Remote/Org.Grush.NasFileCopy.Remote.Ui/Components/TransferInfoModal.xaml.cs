using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class TransferInfoModal : ContentPage
{
  private readonly ExistingRun _run;
  private readonly RsyncLogState _logState;
  private readonly IModalService _modalService;

  public TransferInfoModal(
    ExistingRun run,
    RsyncLogState logState,
    IModalService modalService
    )
  {
    _run = run;
    _logState = logState;
    _modalService = modalService;

    InitializeComponent();

    List<(string label, string? value)> entries =
    [
      ("RunId", _run.RunId),
      ("ProcessState", _run.ProcessState),
      ("CopyFrom", _run.CopyFrom),
      ("Destination", _run.Destination),
      ("LatestTransmittedFile", _logState.LatestTransmittedFile),
      ("TotalKnownBytesTransmitted", _logState.TotalKnownBytesTransmitted.ToString()),
      ("Finished?", (_logState.CompletedSuccessfully is not null).ToString()),
    ];
    if (_logState.CompletedSuccessfully is not null)
      entries.Add(("Completed successfully?", _logState.CompletedSuccessfully.Value.ToString()));

    Table.Add(
      entries.Select(kvp => new TextCell
      {
        Text = kvp.label,
        Detail = kvp.value ?? "",
      })
    );
  }

  private void CloseBtn_OnClicked(object? sender, EventArgs e)
  {
    _modalService.PopModalAsync();
  }
}