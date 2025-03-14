namespace Org.Grush.NasFileCopy.Remote.Ui.Services;

public interface IPopUpService
{
  void RegisterPromptHandler(Page page);
  void UnregisterPromptHandler(Page page);

  Task<string?> DisplayPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel",
    string? placeholder = null, string initialValue = "");
}

internal class PopUpService : IPopUpService
{
  private readonly List<WeakReference<Page>> PromptHandlerStack = [];

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