using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Org.Grush.NasFileCopy.Remote.Share.Ssh;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.Remote.Share;

public class DangerousOperationException(string Message) : InvalidOperationException(Message);

public record SshResult(
  bool Success,
  bool CommandFinished,
  bool? Result = null,
  byte? ExitStatus = null,
  string? Output = null,
  string? Error = null,
  Exception? Exception = null
) : SshResult<bool>(Success, CommandFinished, Result, ExitStatus, Output, Error, Exception)
{
  public new static SshResult Threw(Exception e)
    => new(Success: false, CommandFinished: false, Exception: e);

  public static SshResult Completed(bool success, SshCommand cmd)
    => new(
      Success: success,
      CommandFinished: true,
      Result: success,
      ExitStatus: (byte)cmd.ExitStatus,
      Output: cmd.Result,
      Error: cmd.Error
    );
}

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
  Exception? Exception = null
) where T : struct
{
  public static SshResult<T> Threw(Exception e)
    => new(Success: false, CommandFinished: false, Exception: e);

  public static SshResult<T> Completed(bool success, T result, SshCommand cmd)
    => new(
      Success: success,
      CommandFinished: true,
      Result: result,
      ExitStatus: (byte)cmd.ExitStatus,
      Output: cmd.Result,
      Error: cmd.Error
    );

  public static implicit operator SshResult<T>((T, SshCommand) pair)
      => Completed(true, pair.Item1, pair.Item2);
}


public sealed class TrueNasSshClient(
  ConnectionInfo sshCredentials
) : IAsyncDisposable
{
  private readonly SshClient _sshClient = new(sshCredentials);
  private readonly Regex _dangerousShellQuoteChars = new("\\n|\"");

  public async Task ConnectAsync(CancellationToken cancellationToken)
  {
    await _sshClient.ConnectAsync(cancellationToken);
  }

  public async Task<SshResult> MountAsync(string devPath, string destinationPath, CancellationToken cancellationToken)
  {
    try
    {
      if (devPath.Contains('\''))
        throw new DangerousOperationException("devPath cannot contain '");
      if (_dangerousShellQuoteChars.IsMatch(destinationPath))
        throw new DangerousOperationException("destinationPath contains dangerous characters.");
      using var command = _sshClient.RunCommand($"mount -rw '{devPath}' \"{destinationPath}\"");

      var result = await Task.Factory.FromAsync(
        asyncResult: command.BeginExecute(),
        endMethod: command.EndExecute
      );

      return SshResult.Completed(command.ExitStatus is 0, command);
    } catch (Exception e)
    {
      return SshResult.Threw(e);
    }
  }

  public async Task<SshResult<ImmutableArray<string>>> LsAsync(string path, CancellationToken cancellationToken)
  {
    try
    {
      if (_dangerousShellQuoteChars.IsMatch(path))
        throw new DangerousOperationException("path contains dangerous characters.");
      path = path.Replace("~", "$HOME");

      using var command = _sshClient.RunCommand($"ls -1 \"{path}\" ");

      await Task.Factory.FromAsync(
        asyncResult: command.BeginExecute(),
        endMethod: command.EndExecute
      );

      if (command.ExitStatus is not 0)
        throw new InvalidOperationException($"ls shouldn't throw? It exited {command.ExitStatus}");

      return (
        [
          ..command.Result
            .TrimEnd('\n')
            .Split('\n')
        ],
        command
      );
    }
    catch (Exception e)
    {
      return SshResult<ImmutableArray<string>>.Threw(e);
    }
  }

  public RsyncReader Rsync(
    string copyFrom,
    string destination,
    CancellationToken cancellationToken
  )
  {
    if (copyFrom.Contains('\''))
      throw new DangerousOperationException("copyFrom cannot contain '");
    if (_dangerousShellQuoteChars.IsMatch(destination))
      throw new DangerousOperationException("destination contains dangerous characters.");

    return new RsyncReader(
      _sshClient,
      $"sudo rsync --verbose --archive --no-o --no-g --stats --info=progress2 --info=name0 '{copyFrom}' \"{destination}\"",
      cancellationToken: cancellationToken
    );
  }

  public async ValueTask DisposeAsync()
  {
    _sshClient.Dispose();
  }
}
