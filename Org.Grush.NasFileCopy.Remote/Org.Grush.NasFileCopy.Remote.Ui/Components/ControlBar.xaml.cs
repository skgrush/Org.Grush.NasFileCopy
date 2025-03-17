using Org.Grush.NasFileCopy.Remote.Share;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class ControlBar : ContentView
{
  private readonly IConnectionService _connectionService;
  private readonly IDataService _dataService;

  public ControlBar() : this(
    connectionService: MauiProgram.ServiceProvider.GetRequiredService<IConnectionService>(),
    dataService: MauiProgram.ServiceProvider.GetRequiredService<IDataService>()
  ) { }

  public ControlBar(IConnectionService connectionService, IDataService dataService)
  {
    _connectionService = connectionService;
    _dataService = dataService;

    InitializeComponent();

    ConnectBtn.IsEnabled = RefreshBtn.IsEnabled = false;
    DisconnectBtn.IsVisible = false;

    _connectionService.ConnectionChanged += ConnectionChanged;
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
    var configPage = Handler?.MauiContext?.Services.GetService<ConfigurationModal>();

    Navigation.PushModalAsync(configPage).ConfigureAwait(ConfigureAwaitOptions.None);
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