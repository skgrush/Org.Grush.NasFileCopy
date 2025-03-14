using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class ConfigurationModal : ContentPage
{
  private readonly IStorageService _storage;

  public ConfigurationModal(
    IStorageService storage
  )
  {
    InitializeComponent();

    _storage = storage;

    UpdateStorageConfig(_storage.ReadConfig());
    _storage.ConfigChanged += (_, tuple) => UpdateStorageConfig(tuple.newConfig);
  }

  private void UpdateStorageConfig(StorageConfig config)
  {
    HostnameEntry.Text = config.Hostname ?? "";
    UsernameEntry.Text = config.Username ?? "";
    PrivateKeyEntry.Text = config.PrivateKey ?? "";
  }

  private async void BackToolbarBtn_OnClicked(object? sender, EventArgs e)
  {
    await Navigation.PopModalAsync().ConfigureAwait(false);
  }

  private void SaveBtn_OnClicked(object? sender, EventArgs e)
  {
    _storage.WriteConfig(oldConfig => oldConfig with
    {
      Hostname = HostnameEntry.Text is { Length: >0} h ? h : null,
      Username = UsernameEntry.Text is { Length: >0} u ? u : null,
      PrivateKey = PrivateKeyEntry.Text is { Length: >0} p ? p : null,
    });
  }

}