using System.Diagnostics;
using Org.Grush.NasFileCopy.ClientSide.Shared;
using Org.Grush.NasFileCopy.Structures;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.ClientSide.Maui;

public enum ConnectState
{
  Uninitialized = 0,
  Connecting = 1,
  Success = 2,
  Error = 3,
}

public partial class MainPage : ContentPage
{
  int count = 0;

  private ConnectionInfo? ConnectionInfo { get; set; }
  private ConnectState ConnectState;
  private ListCommandAcceptableValues? ListResult { get; set; }

  private IReadOnlyList<string> MountPoints => ListResult?.AcceptableSourceNames ?? [];
  private IReadOnlyList<string> Destinations => ListResult?.AcceptableDestinationLabels ?? [];

  public MainPage()
  {
    InitializeComponent();
  }

  private void OnConnectionEntryChanged(object sender, EventArgs e)
  {
    ConnectionInfo = null;
    ListResult = null;
    CopySourceMountPointPicker.SelectedIndex =
      CopyDestinationLabelPicker.SelectedIndex = -1;
  }

  private void OnCopyClicked(object sender, EventArgs e)
  {
    string? sourceMountPoint = CopySourceMountPointPicker.SelectedItem as string;
    string? destinationLabel = CopyDestinationLabelPicker.SelectedItem as string;

    if (sourceMountPoint is null || destinationLabel is null)
      return; // TODO


  }

  private void OnConnectClicked(object sender, EventArgs e)
  {
    var hostname = ConnectionHostname.Text;
    var username = ConnectionUsername.Text;
    var password = ConnectionPassword.Text;

    if (hostname.Length > 0 && username.Length > 0 && password.Length > 0)
    {
      ConnectionInfo = new ConnectionInfo(
        host: hostname,
        username: username,
        new PasswordAuthenticationMethod(username, password)
      )
      {
        RetryAttempts = 2,
        Timeout = TimeSpan.FromSeconds(20)
      };
    }

    // TODO: how do call task goodly?
    var token = new CancellationToken();
    Connect(token).ConfigureAwait(false);
  }

  private async Task Connect(CancellationToken token)
  {
    if (ConnectionInfo is null)
    {
      await Dispatcher.DispatchAsync(() =>UpdateConnectionState(ConnectState.Error, message: "Missing credentials"));
      return;
    }

    await Dispatcher.DispatchAsync(() => UpdateConnectionState(ConnectState.Connecting));

    try
    {
      var ssh = new NasComSshClient(ConnectionInfo, "/opt/");

      var listResult = await ssh.ListDevices(token);

      if (listResult is not null)
        await Dispatcher.DispatchAsync(() => UpdateConnectionState(ConnectState.Success, listResult: listResult));
      else
        await Dispatcher.DispatchAsync(() =>
          UpdateConnectionState(ConnectState.Error, message: "Connected but failed to receive the list"));
    }
    catch (Exception e)
    {
      await Dispatcher.DispatchAsync(() =>UpdateConnectionState(ConnectState.Error, message: e.Message));
    }
  }

  private async Task Copy(
    string sourceMountPoint,
    string destinationLabel,
    CancellationToken token
  )
  {
    if (ConnectionInfo is null)
    {
      // TODO: error missing creds
      return;
    }

    if (ConnectState is not ConnectState.Success)
    {
      // TODO: connect first
      return;
    }

    if (ListResult is null)
    {
      // TODO: error not connected
      return;
    }

    try
    {
      var ssh = new NasComSshClient(ConnectionInfo, "/opt/");

      var handler = new UiStreamOutputHandler(this);
      var listResult = await ssh.Copy(token, sourceMountPoint, destinationLabel, handler);

      // TODO: success
    }
    catch (Exception e)
    {
      await Dispatcher.DispatchAsync(() =>
        AppendCopyOutput($"\n\nLocal {e.GetType()}: {e.Message}")
      );
    }
  }

  private void UpdateConnectionState(
    ConnectState connectState,
    ListCommandAcceptableValues? listResult = null,
    string? message = null
  )
  {
    ConnectState = connectState;

    ConnectionStatusLabel.Text = connectState switch
    {
      ConnectState.Connecting => "Connecting...",
      ConnectState.Success => "Successfully connected!",
      ConnectState.Error => "Error connecting",
      _ or ConnectState.Uninitialized => "",
    };

    ConnectionMessageLabel.Text = message ?? "";

    ListResult = listResult;
  }

  private void AppendCopyOutput(string newOutput)
  {
    const int maxOutputLength = 5000;

    if (CopyOutput.Text.Length + newOutput.Length > maxOutputLength)
    {
      CopyOutput.Text = CopyOutput.Text[(maxOutputLength - newOutput.Length)..] + newOutput;
    }
    else
    {
      CopyOutput.Text += newOutput;
    }
  }

  // private void OnCounterClicked(object sender, EventArgs e)
  // {
  //   count++;
  //
  //   if (count == 1)
  //     CounterBtn.Text = $"Clicked {count} time";
  //   else
  //     CounterBtn.Text = $"Clicked {count} times";
  //
  //   SemanticScreenReader.Announce(CounterBtn.Text);
  // }

  class UiStreamOutputHandler(MainPage ui) : BaseStreamOutputHandler
  {
    private readonly MainPage _ui = ui;

    protected override async Task WriteFromStream(string text, StreamType streamType, CancellationToken token)
    {
      await _ui.Dispatcher.DispatchAsync(() => _ui.AppendCopyOutput(text));
    }

    public override async Task HandleEnd(CopyCommandExitCodes exitCode, string runnerError, CancellationToken token)
    {
      string messageToAppend;
      if (exitCode is CopyCommandExitCodes.OkOrHelp)
      {
        messageToAppend = "\n\n\nSuccess!";
      }
      else
      {
        var exitMessage = ExitCodeToMessage(exitCode);
        messageToAppend = $"\n\n\nError: exit status {(int)exitCode} ({exitMessage})\n{runnerError}";
      }

      await _ui.Dispatcher.DispatchAsync(() => _ui.AppendCopyOutput(messageToAppend));
    }
  }
}