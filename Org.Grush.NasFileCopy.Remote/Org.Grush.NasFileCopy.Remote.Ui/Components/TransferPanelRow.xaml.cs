using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Org.Grush.NasFileCopy.Remote.Share;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class TransferPanelRow : ContentView
{
  private readonly ITrueNasClient _trueNasClient;

  // TODO: need to clean this up, maybe with a behavior
  private CancellationTokenSource RunChangedCanceller { get; set; } = new();

  public ObservableState? State { get; private set; }

  public TransferPanelRow() : this(
    trueNasClient: MauiProgram.ServiceProvider.GetRequiredService<ITrueNasClient>()
  ) { }

  public TransferPanelRow(
    ITrueNasClient trueNasClient
  )
  {
    _trueNasClient = trueNasClient;

    InitializeComponent();
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
      token.ThrowIfCancellationRequested();

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
    public ulong? TotalKnownBytesTransmitted => RsyncLogState?.TotalKnownBytesTransmitted;

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

        if (processState[0] is 'R')
        {
          if (RsyncLogState?.LatestTransmittedFile is null)
            return "LOADING";
          return "RUNNING";
        }

        if (processState[0] is char sleepLetter and ('S' or 'D'))
          return $"SLEEPING({sleepLetter})";
        if (processState[0] is 'T' or 't')
          return "STOPPED";
        if (processState[0] is char diedLetter and ('Z' or 'X'))
          return $"DIED({diedLetter})";

        return $"[??={processState}]";
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
  }


  public static readonly BindableProperty RunProperty =
    BindableProperty.Create(nameof(Run), typeof(ExistingRun?), typeof(TransferPanelRow),
      propertyChanged: (bindable, value, newValue) =>
        ((TransferPanelRow)bindable).RunChanged(newValue as ExistingRun?)
    );
}