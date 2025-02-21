using Org.Grush.NasFileCopy.Structures;
using Renci.SshNet;

namespace Org.Grush.NasFileCopy.ClientSide.Shared;

public enum StreamType
{
  Out = 1,
  Err = 2,
}

public abstract class BaseStreamOutputHandler
{
  protected virtual int OutputDelayMs { get; } = 10;

  protected abstract Task WriteFromStream(string text, StreamType streamType, CancellationToken token);

  public abstract Task HandleEnd(CopyCommandExitCodes exitCode, string runnerError, CancellationToken token);

  public virtual string ExitCodeToMessage(CopyCommandExitCodes exitCode)
  {
    return !Enum.IsDefined(exitCode)
      ? "UNDEFINED" :
      exitCode.ToString();
  }

  public virtual async Task CheckOutputAndReportProgressAsync(
    SshCommand sshCommand,
    IAsyncResult asyncResult,
    StreamReader streamReader,
    StreamType streamType,
    CancellationToken cancellationToken
  )
  {
    while (!asyncResult.IsCompleted || !streamReader.EndOfStream)
    {
      if (cancellationToken.IsCancellationRequested)
      {
        sshCommand.CancelAsync();
      }

      cancellationToken.ThrowIfCancellationRequested();

      var remaining = await streamReader.ReadToEndAsync(cancellationToken);

      if (!string.IsNullOrEmpty(remaining))
      {
        await WriteFromStream(remaining, streamType, cancellationToken);
      }

      // wait 10 ms
      await Task.Delay(OutputDelayMs, cancellationToken);
    }
  }
}

public class ConsoleStreamOutputHandler : BaseStreamOutputHandler
{
  protected override Task WriteFromStream(string text, StreamType streamType, CancellationToken token)
  {
    Console.Write(text);
    return Task.CompletedTask;
  }

  public override Task HandleEnd(CopyCommandExitCodes exitCode, string runnerError, CancellationToken token)
  {
    if (exitCode is CopyCommandExitCodes.OkOrHelp)
    {
      Console.WriteLine();
    }
    else
    {
      var exitMessage = ExitCodeToMessage(exitCode);
      Console.WriteLine($"Error, exit status {(int)exitCode} ({exitMessage}): {runnerError}");
    }

    return Task.CompletedTask;
  }
}