using Org.Grush.NasFileCopy.Remote.Share.Exceptions;

namespace Org.Grush.NasFileCopy.Remote.Share;

public interface ISudoPrompterFactory
{
  public ISudoPrompter Create(Func<Task<string?>> prompt);
}

public interface ISudoPrompter : IAsyncDisposable
{
  /// <summary>
  ///
  /// </summary>
  /// <returns></returns>
  /// <exception cref="InvalidOperationException">If disposed.</exception>
  /// <exception cref="PromptCancelledException">If the sudo prompt was cancelled or blank.</exception>
  Task<string> GetPasswordAsync();
}

internal sealed class SudoPrompterFactory : ISudoPrompterFactory
{
  public ISudoPrompter Create(Func<Task<string?>> prompt)
  {
    return new SudoPrompter(prompt);
  }
}

internal sealed class SudoPrompter : ISudoPrompter
{
  private Func<Task<string?>>? Prompt { get; set; }
  private string? CachedPassword { get; set; }

  public SudoPrompter(Func<Task<string?>> prompt)
  {
    Prompt = prompt;
  }

  /// <inheritdoc />
  public async Task<string> GetPasswordAsync()
  {
    if (Prompt is null)
      throw new InvalidOperationException("SudoPrompter disposed");
    if (CachedPassword is not null)
      return CachedPassword;

    var promptResult = await Prompt();
    if (promptResult is null or "")
      throw new PromptCancelledException("Sudo prompt cancelled");

    return promptResult;
  }

  public ValueTask DisposeAsync()
  {
    CachedPassword = null;
    Prompt = null;

    return default;
  }
}