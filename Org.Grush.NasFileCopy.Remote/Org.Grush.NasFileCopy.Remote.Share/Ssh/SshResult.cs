using System.Diagnostics.CodeAnalysis;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share.Ssh;

internal record SshResult<T>(
  [property: MemberNotNullWhen(true, "Result")]
  bool Success,
  [property: MemberNotNullWhen(true, "ExitStatus")]
  [property: MemberNotNullWhen(true, "Output")]
  [property: MemberNotNullWhen(true, "Error")]
  [property: MemberNotNullWhen(false, "Exception")]
  bool CommandFinished,
  // ReSharper disable once InconsistentNaming
  T? _Result = null,
  byte? ExitStatus = null,
  string? Output = null,
  string? Error = null,
  Exception? Exception = null,
  string? Commentary = null
) where T : struct
{
  public T Result => _Result!.Value;

  public static implicit operator UiResult<T>(SshResult<T> result)
    => result switch
    {
      { Success: true, Result: T r } => UiResult<T>.Ok(r),
      { Commentary: string c } => UiResult<T>.Err(c),
      { Exception: Exception e } => UiResult<T>.Err(e.ToString()),
      { CommandFinished: false } => throw new Exception(),
      _ => UiResult<T>.Err($"Uncommented command exited with {result.ExitStatus} and error output: {result.Error}"),
    };

  public UiResult<R> ToUiError<R>() =>
    this switch
    {
      { Success: true } => throw new NotSupportedException(),
      { Commentary: not null } => UiResult<R>.Err(Commentary),
      { Exception: not null } => UiResult<R>.Err(Exception.ToString()),
      { CommandFinished: false } => throw new(), // SHOULD never be able to have exception and CommandFinished in this state
      _ => UiResult<R>.Err($"Uncommented command exited with {ExitStatus} and error output: {Error}"),
    };


  public static SshResult<T> Threw(Exception e)
    => new(Success: false, CommandFinished: false, Exception: e);

  public static SshResult<T> Completed(bool success, T? result, SshCommand cmd)
  {
    if (success && result is null)
      throw new NotSupportedException($"Call to {nameof(SshResult<T>)}.{nameof(Completed)}() called with Success=false and Result=null");

    return new SshResult<T>(
      Success: success,
      CommandFinished: true,
      _Result: result,
      ExitStatus: (byte?)cmd.ExitStatus,
      Output: cmd.Result,
      Error: cmd.Error
    );
  }

  public static SshResult<T> Completed(bool success, T? result, byte? exitStatus, string? output)
  {
    if (success && result is null)
      throw new NotSupportedException($"Call to {nameof(SshResult<T>)}.{nameof(Completed)}() called with Success=false and Result=null");

    return new SshResult<T>(
      Success: success,
      CommandFinished: true,
      _Result: result,
      ExitStatus: exitStatus,
      Output: output,
      Error: null
    );
  }
}