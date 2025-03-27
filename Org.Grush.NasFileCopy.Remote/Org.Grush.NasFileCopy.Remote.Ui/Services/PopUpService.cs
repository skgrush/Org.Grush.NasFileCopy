namespace Org.Grush.NasFileCopy.Remote.Ui.Services;

public interface IPopUpService
{
  /// <summary>Register a weakref of the page as a popup handler. The last registered will be used.</summary>
  void RegisterPromptHandler(Page page);

  /// <summary>Unregister the page and tidy up any other weakrefs.</summary>
  void UnregisterPromptHandler(Page page);

  Task<string?> DisplayPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel",
    string? placeholder = null, string initialValue = "");

  Task DisplayAlertAsync(string? title, string message, string? cancel = "Cancel");
  Task<string?> DisplayPasswordPromptAsync(string title, string message, string accept, string cancel);
}

internal class PopUpService : IPopUpService
{
  private List<WeakReference<Page>> PromptHandlerStack { get; } = [];

  public void RegisterPromptHandler(Page page)
    => PromptHandlerStack.Add(new WeakReference<Page>(page));

  public void UnregisterPromptHandler(Page page)
  => PromptHandlerStack.RemoveAll(weakRef => !weakRef.TryGetTarget(out var p) || ReferenceEquals(p, page));

  public async Task<string?> DisplayPromptAsync(string title, string message, string accept, string cancel,
    string? placeholder, string initialValue)
  {
    var page = GetLastHandler();

    return await page.DisplayPromptAsync(title: title, message: message, accept: accept, cancel: cancel,
          placeholder: placeholder, initialValue: initialValue);
  }

  public async Task<string?> DisplayPasswordPromptAsync(string title, string message, string accept, string cancel)
  {
    var page = GetLastHandler();

    return await page.DisplayPromptAsync(title: title, message: message, accept: accept, cancel: cancel,
      keyboard: Keyboard.Password
    );
  }

  public async Task DisplayAlertAsync(string? title, string message, string? cancel = "Cancel")
  {
    var page = GetLastHandler();

    await page.DisplayAlert(title: title, message: message, cancel: cancel);
  }

  private Page GetLastHandler()
  {
    while(true)
    {
      var lastHandler = PromptHandlerStack.Last();
      if (lastHandler.TryGetTarget(out var page))
        return page;
      PromptHandlerStack.RemoveAt(-1);
    }
  }
}