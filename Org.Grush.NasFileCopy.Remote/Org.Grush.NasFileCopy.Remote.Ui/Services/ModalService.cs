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
) : IModalService
{
  private INavigation? Navigation { get; set; }

  public void RegisterNavigation(INavigation navigation)
    => Navigation = navigation;

  public async Task PopModalAsync()
  {
    await Navigation!.PopModalAsync().ConfigureAwait(true);
  }

  public async Task OpenModal<T>()
    where T : Page
  {
    var modal = serviceProvider.GetRequiredService<T>();
    await Navigation!.PushModalAsync(modal).ConfigureAwait(true);
  }
  public async Task OpenModal<T>(T modal)
    where T : Page
  {
    await Navigation!.PushModalAsync(modal).ConfigureAwait(true);
  }

  public async Task OpenTransferPanelModal(ExistingRun run, RsyncLogState logState)
  {
    await OpenModal(new TransferInfoModal(
      run: run,
      logState: logState,
      modalService: this
    ));
  }

  public async Task OpenConfigurationModal()
  {
    await OpenModal<ConfigurationModal>();
  }
}