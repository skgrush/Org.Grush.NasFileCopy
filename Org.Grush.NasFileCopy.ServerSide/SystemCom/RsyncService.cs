using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Org.Grush.NasFileCopy.ServerSide.SystemCom;

public class RsyncService
{
  private const string LogFile = "/var/log/rsync.log";

  public static readonly Regex SimplePathRe = new("^[a-z0-9/]+$", RegexOptions.IgnoreCase);

  public async Task<bool> Sync(string src, string dest)
  {
    if (!SimplePathRe.IsMatch(src) || !SimplePathRe.IsMatch(dest))
      throw new ArgumentException($"invalid characters in src or dest; must be alphanumeric path: {src}|{dest}");

    var process = new Process();

    process.StartInfo.FileName = "rsync";
    process.StartInfo.WorkingDirectory = "/bin";
    process.StartInfo.Arguments = $"--verbose --log-file={LogFile} --archive --no-o --no-g {src} {dest}";

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