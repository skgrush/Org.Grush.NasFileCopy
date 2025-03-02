using System.Diagnostics.CodeAnalysis;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share.Ssh;

public record SshResult<T>(
  [property: MemberNotNullWhen(true, nameof(Result))]
  bool Success,
  [property: MemberNotNullWhen(true, nameof(ExitStatus))]
  [property: MemberNotNullWhen(true, nameof(Output))]
  [property: MemberNotNullWhen(true, nameof(Error))]
  [property: MemberNotNullWhen(false, nameof(Exception))]
  bool CommandFinished,
  T? Result = null,
  byte? ExitStatus = null,
  string? Output = null,
  string? Error = null,
  Exception? Exception = null,
  string? Commentary = null
) where T : struct
{
  public static SshResult<T> Threw(Exception e)
    => new(Success: false, CommandFinished: false, Exception: e);

  public static SshResult<T> Completed(bool success, T? result, SshCommand cmd)
    => new(
      Success: success,
      CommandFinished: true,
      Result: result,
      ExitStatus: (byte)cmd.ExitStatus,
      Output: cmd.Result,
      Error: cmd.Error
    );
}