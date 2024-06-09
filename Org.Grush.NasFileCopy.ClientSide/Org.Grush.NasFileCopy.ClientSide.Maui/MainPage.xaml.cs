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

      await Dispatcher.DispatchAsync(() =>
      {
        UpdateConnectionState(ConnectState.Success, listResult: listResult);
      });
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

      var listResult = await ssh.Copy(token, sourceMountPoint, destinationLabel);

      // TODO: success
    }
    catch (Exception e)
    {
      // TODO: error
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

    CopySourceMountPointPicker.ItemsSource =
      listResult?.AcceptableSourceNames
        .ToList() ?? [];
    CopyDestinationLabelPicker.ItemsSource =
      listResult?.AcceptableDestinationLabels
        .ToList() ?? [];
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
}