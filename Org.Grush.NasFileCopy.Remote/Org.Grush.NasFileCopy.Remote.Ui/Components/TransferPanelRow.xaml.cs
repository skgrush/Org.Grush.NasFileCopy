using System.ComponentModel;
using Org.Grush.NasFileCopy.Remote.Share;
using Org.Grush.NasFileCopy.Remote.Share.Extensions;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class TransferPanelRow : ContentView
{
  private readonly ITrueNasClient _trueNasClient;
  private readonly IModalService _modalService;

  private CancellationTokenSource RunChangedCanceller { get; set; } = new();

  public ObservableState? State { get; private set; }

  public TransferPanelRow() : this(
    trueNasClient: MauiProgram.ServiceProvider.GetRequiredService<ITrueNasClient>(),
    modalService: MauiProgram.ServiceProvider.GetRequiredService<IModalService>()
  ) { }

  public TransferPanelRow(
    ITrueNasClient trueNasClient,
    IModalService modalService
  )
  {
    _trueNasClient = trueNasClient;
    _modalService = modalService;

    InitializeComponent();

    Unloaded += (_, _) => RunChangedCanceller.Cancel();
  }

  public ExistingRun Run
  {
    get => (ExistingRun)GetValue(RunProperty);
    set => SetValue(RunProperty, value);
  }

  private void RunChanged(ExistingRun? newRun)
  {
    RunChangedCanceller.Cancel();
    RunChangedCanceller.Dispose();
    RunChangedCanceller = new();

    if (newRun is null)
      return;

    var token = RunChangedCanceller.Token;

    Listen(newRun.Value, RunChangedCanceller.Token);
  }

  private async void Listen(ExistingRun run, CancellationToken token)
  {
    var loadResult = await _trueNasClient.LoadLogReaderAsync(
      runId: run.RunId,
      overwriteAndDisposeExisting: false,
      cancellationToken: token
    );

    if (!loadResult.IsOk)
    {
      if (loadResult.Error.Contains("Failed to ReadFromLockFileGivenAsync"))
        State = new(run, null, overrideStatus: "RUN NOT FOUND");
      else if (loadResult.Error.Contains("Proc folder "))
        State = new(run, null, overrideStatus: "PROC NOT FOUND");
      else if (loadResult.Error.Contains("found PID="))
        State = new(run, null, overrideStatus: "PID MISMATCH");
      else if (loadResult.Error.Contains("Failed"))
        State = new(run, null, overrideStatus: "FAILED");
      else
        State = new(run, null, overrideStatus: "UNKNOWN ERROR");

      return;
    }

    State = new(run, null);

    var asyncEnum = _trueNasClient.ListenToLocallyConnectedLog(
      runId: run.RunId,
      static msg =>
      {
        Console.WriteLine(msg);
        return Task.CompletedTask;
      },
      cancellationToken: token
    );

    if (asyncEnum is null)
      return;

    await foreach (var item in asyncEnum)
    {
      if (token.IsCancellationRequested)
        break;

      State.Update(item);
    }
  }

  public class ObservableState : INotifyPropertyChanged
  {
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly ExistingRun _run;
    private readonly string? _overrideStatus;
    private RsyncLogState? RsyncLogState { get; set; }

    public bool Completed => RsyncLogState?.CompletedSuccessfully is not null;
    public string? LatestTransmittedFile => RsyncLogState?.LatestTransmittedFile;
    public string TotalKnownBytesTransmitted
    {
      get
      {
        if (RsyncLogState?.TotalKnownBytesTransmitted is not { } b)
          return "N/A";

        return b.FormatToBytes();
      }
    }

    public string Status {
      get
      {
        if (_overrideStatus is not null)
          return _overrideStatus;
        if (RsyncLogState?.CompletedSuccessfully is true)
          return "DONE";
        if (RsyncLogState?.CompletedSuccessfully is false)
          return "FAILED";

        if (_run.ProcessState is not {} processState)
          return "PROC NOT FOUND";

        var processLetter = processState[0];
        if (processLetter is 'R' && RsyncLogState?.LatestTransmittedFile is null)
          return "LOADING";

        return processLetter switch
        {
          'R' or 'r' => "RUNNING",
          'T' or 't' => "STOPPED",
          'S' or 'D' => $"SLEEPING({processLetter})",
          'Z' or 'X' => $"DIED({processLetter})",
          _          => $"[??={processState}]",
        };
      }
    }

    public ObservableState(ExistingRun run, RsyncLogState? rsyncLogState, string? overrideStatus = null)
    {
      _run = run;
      _overrideStatus = overrideStatus;
      Update(rsyncLogState);
    }

    public void Update(RsyncLogState? state)
    {
      var old = RsyncLogState;
      RsyncLogState = state;

      if (state?.CompletedSuccessfully != old?.CompletedSuccessfully)
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Completed)));
      if (state?.LatestTransmittedFile != old?.LatestTransmittedFile)
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LatestTransmittedFile)));
      if (state?.TotalKnownBytesTransmitted != old?.TotalKnownBytesTransmitted)
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalKnownBytesTransmitted)));

      if (state != old)
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
    }

    public async Task OpenModal(IModalService modalService)
    {
      if (RsyncLogState is not null)
        await modalService.OpenTransferPanelModal(_run, RsyncLogState.Value);
    }
  }

  private async void InfoBtn_OnClicked(object? sender, EventArgs e)
  {
    if (State is not null)
      await State.OpenModal(_modalService);
  }

  private void StopBtn_OnClicked(object? sender, EventArgs e)
  {
    throw new NotImplementedException();
  }


  public static readonly BindableProperty RunProperty =
    BindableProperty.Create(nameof(Run), typeof(ExistingRun?), typeof(TransferPanelRow),
      propertyChanged: (bindable, value, newValue) =>
        ((TransferPanelRow)bindable).RunChanged(newValue as ExistingRun?)
    );
}