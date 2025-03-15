using Org.Grush.NasFileCopy.Remote.Share;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class ControlBar : ContentView
{
  private readonly ITrueNasClient _client;
  private readonly IStorageService _storage;
  private readonly IConnectionService _connectionService;

  public ControlBar() : this(
    MauiProgram.ServiceProvider.GetRequiredService<ITrueNasClient>(),
    MauiProgram.ServiceProvider.GetRequiredService<IStorageService>(),
    MauiProgram.ServiceProvider.GetRequiredService<IConnectionService>()
  )
  {
  }

  public ControlBar(ITrueNasClient client, IStorageService storage, IConnectionService connectionService)
  {
    _client = client;
    _storage = storage;
    _connectionService = connectionService;

    InitializeComponent();

    ConnectBtn.IsEnabled = RefreshBtn.IsEnabled = false;

    _connectionService.ConnectionChanged += ConnectionChanged;
  }

  private async void ConnectBtn_OnClicked(object? sender, EventArgs e)
  {
    await _connectionService.ConnectAsync().ConfigureAwait(true);
  }

  private void ConfigBtn_OnClicked(object? sender, EventArgs e)
  {
    var configPage = Handler?.MauiContext?.Services.GetService<ConfigurationModal>();

    Navigation.PushModalAsync(configPage).ConfigureAwait(ConfigureAwaitOptions.None);
  }

  private void RefreshBtn_OnClicked(object? sender, EventArgs e)
  {
    throw new NotImplementedException();
  }

  private void ConnectionChanged(object? sender, ConnectionConfig config)
  {
    ConnectBtn.IsEnabled = config.ConnectionInfo is not null;
    RefreshBtn.IsEnabled = config.IsConnected;
  }
}