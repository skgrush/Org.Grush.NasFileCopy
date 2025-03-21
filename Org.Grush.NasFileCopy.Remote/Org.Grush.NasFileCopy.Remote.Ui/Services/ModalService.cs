using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Org.Grush.NasFileCopy.Remote.Ui.Components;

namespace Org.Grush.NasFileCopy.Remote.Ui.Services;

public interface IModalService
{
  Task PopModalAsync();
  Task OpenTransferPanelModal(ExistingRun run, RsyncLogState logState);
  Task OpenConfigurationModal();
  void RegisterNavigation(INavigation navigation);
}

internal class ModalService(
  IServiceProvider serviceProvider
  // TransferInfoModal transferInfoModal,
  // ConfigurationModal configurationModal
) : IModalService
{
  private INavigation? Navigation { get; set; }

  public void RegisterNavigation(INavigation navigation)
    => Navigation = navigation;

  public async Task PopModalAsync()
  {
    // if (Navigation is null)
    //   throw new InvalidOperationException("Navigation is null");

    await Navigation!.PopModalAsync().ConfigureAwait(true);
  }

  public async Task OpenModal<T>(Action<T>? callback = null)
    where T : Page
  {
    var modal = serviceProvider.GetRequiredService<T>();
    callback?.Invoke(modal);

    await Navigation!.PushModalAsync(modal).ConfigureAwait(true);
  }

  public async Task OpenTransferPanelModal(ExistingRun run, RsyncLogState logState)
  {
    await OpenModal<TransferInfoModal>(transferInfoModal =>
    {
      transferInfoModal.Run = run;
      transferInfoModal.LogState = logState;
    });
  }

  public async Task OpenConfigurationModal()
  {
    await OpenModal<ConfigurationModal>();
  }
}