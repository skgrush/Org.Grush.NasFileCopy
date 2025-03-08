using System.Diagnostics.CodeAnalysis;

namespace Org.Grush.NasFileCopy.Remote.Share;

public class UiResult<T>
{
  private readonly T? _result;
  private readonly string? _error;

  [MemberNotNullWhen(true, nameof(Value))]
  [MemberNotNullWhen(false, nameof(Error))]
  public bool IsOk => _error == null;
  public T Value => IsOk ? _result! : throw new InvalidOperationException("UiResult not OK");
  public string? Error => _error;

  private UiResult(string? error, T? result)
  {
    _error = error;
    _result = result;
  }

  public static UiResult<T> Ok(T value) => new(null, value);
  public static UiResult<T> Err(string err) => new(err, default);

  public static implicit operator UiResult<T>(T value) => Ok(value);
}

public static class UiResult
{
  public static async Task<UiResult<T>> ExecuteAsync<T>(Func<Task<T>> action, Func<Exception, string>? errorHandler = null)
  {
    try
    {
      return await action();
    }
    catch (Exception e)
    {
      return UiResult<T>.Err(
        errorHandler is null
          ? e.Message
          : errorHandler(e)
      );
    }
  }

}