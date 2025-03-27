using Org.Grush.NasFileCopy.Remote.Share;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class ControlBar : ContentView
{
  private readonly IConnectionService _connectionService;
  private readonly IDataService _dataService;
  private readonly IModalService _modalService;

  public ControlBar() : this(
    connectionService: MauiProgram.ServiceProvider.GetRequiredService<IConnectionService>(),
    dataService: MauiProgram.ServiceProvider.GetRequiredService<IDataService>(),
    modalService: MauiProgram.ServiceProvider.GetRequiredService<IModalService>()
  ) { }

  public ControlBar(
    IConnectionService connectionService,
    IDataService dataService,
    IModalService modalService
  )
  {
    _connectionService = connectionService;
    _dataService = dataService;
    _modalService = modalService;

    InitializeComponent();

    ConnectBtn.IsEnabled = RefreshBtn.IsEnabled = false;
    DisconnectBtn.IsVisible = false;

    _connectionService.ConnectionChanged += ConnectionChanged;
    ConnectionChanged(this, _connectionService.Config);
  }

  private async void ConnectBtn_OnClicked(object? sender, EventArgs e)
  {
    ConnectBtn.IsEnabled = RefreshBtn.IsEnabled = ConfigBtn.IsEnabled = false;
    var result = await _connectionService.ConnectAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

    if (!result.validTry)
    {
      ConnectBtn.IsEnabled = ConfigBtn.IsEnabled = true;
    }
  }

  private void ConfigBtn_OnClicked(object? sender, EventArgs e)
  {
    _modalService.OpenConfigurationModal();
  }

  private void RefreshBtn_OnClicked(object? sender, EventArgs e)
  {
    _dataService.RefreshAsync().ConfigureAwait(true);
  }

  private void ConnectionChanged(object? sender, ConnectionConfig config)
  {
    ConnectBtn.IsEnabled = config.ConnectionInfo is not null;
    ConnectBtn.IsVisible = !config.IsConnected;
    DisconnectBtn.IsVisible = config.IsConnected;

    ConfigBtn.IsEnabled = !config.IsConnected;
    RefreshBtn.IsEnabled = config.IsConnected;
  }

  private async void DisconnectBtn_OnClicked(object? sender, EventArgs e)
  {
    await _connectionService.DisconnectAsync().ConfigureAwait(true);
  }
}