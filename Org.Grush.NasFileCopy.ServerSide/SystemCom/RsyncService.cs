using System.Diagnostics;

namespace Org.Grush.NasFileCopy.ServerSide.SystemCom;

public class RsyncService
{
  private const string LogFile = "/var/log/rsync.log";

  public async Task<bool> Sync(string src, string dest)
  {
    if (src.Contains('\'') || dest.Contains('\''))
      throw new ArgumentException($"invalid src or dest due to apostrophe {src}|{dest}");
    
    var process = new Process();

    process.StartInfo.FileName = "rsync";
    process.StartInfo.WorkingDirectory = "/bin";
    process.StartInfo.Arguments = $"--verbose --log-file={LogFile} --archive '{src}' '{dest}'";

    process.StartInfo.UseShellExecute = false;
    process.StartInfo.CreateNoWindow = true;
    process.Start();

    await process.WaitForExitAsync();

    if (process.ExitCode is not 0)
    {
      Console.WriteLine($"Rsync exited with code {process.ExitCode}; log file located at {LogFile}");
      return false;
    }

    return true;
  }
}