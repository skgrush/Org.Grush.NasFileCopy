using Org.Grush.NasFileCopy.Remote.Share;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class ControlBar : ContentView
{
  private ITrueNasClient _client = null!;
  private IStorageService _storage = null!;
  private ConnectionService _connectionService = null!;

  public ControlBar()
  {
    InitializeComponent();

    ConnectBtn.IsEnabled = RefreshBtn.IsEnabled = false;

    HandlerChanged += (sender, args) =>
    {
      var services = Handler!.MauiContext!.Services;
      _client = services.GetRequiredService<ITrueNasClient>();
      _storage = services.GetRequiredService<IStorageService>();
      _connectionService = services.GetRequiredService<ConnectionService>();

      _connectionService.ConnectionChanged += ConnectionChanged;
    };
  }

  private void ConnectBtn_OnClicked(object? sender, EventArgs e)
  {
    _connectionService.ConnectAsync();
  }

  private void ConfigBtn_OnClicked(object? sender, EventArgs e)
  {
    throw new NotImplementedException();
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