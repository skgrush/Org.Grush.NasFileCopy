using System.Diagnostics;

namespace Org.Grush.NasFileCopy.ServerSide.SystemCom;

public class MountService
{
  public async Task<IReadOnlyList<MountPoint>> ReadMounts()
  {
    var process = new Process();

    process.StartInfo.FileName = "mount";
    process.StartInfo.WorkingDirectory = "/bin";

    process.StartInfo.UseShellExecute = false;
    process.StartInfo.CreateNoWindow = true;
    process.StartInfo.RedirectStandardOutput = true;
    process.Start();

    var outputString = await process.StandardOutput.ReadToEndAsync();

    await process.WaitForExitAsync();

    return outputString
      .Split('\n')
      .Where(line => line.Length > 2)
      .Select(MountPoint.From)
      .ToList();
  }

  public async Task<bool> Mount(string label, string mountPoint)
  {
    if (label.Contains('\'') || mountPoint.Contains('\''))
    {
      Console.WriteLine($"Error: label or mountPoint is invalid due to apostrophe: {label}|{mountPoint}");
      return false;
    }

    var mounts = await ReadMounts();

    if (!File.Exists(mountPoint))
    {
      Console.WriteLine($"Creating mount point {mountPoint}");
      Directory.CreateDirectory(mountPoint);
    }
    else if(mounts.Any(m => m.Path == mountPoint))
    {
      Console.WriteLine($"Error: cannot mount to {mountPoint} as it is already in use");
      return false;
    }

    var process = new Process();

    process.StartInfo.FileName = "mount";
    process.StartInfo.WorkingDirectory = "/bin";
    process.StartInfo.Arguments = $"-rw LABEL='{label}' '{mountPoint}'";

    process.StartInfo.UseShellExecute = false;
    process.StartInfo.CreateNoWindow = true;
    process.Start();

    await process.WaitForExitAsync();

    return process.ExitCode is 0;
  }
}

public record MountPoint(
  string Name,
  string Path,
  string Type,
  string RawFlags
)
{
  public static MountPoint From(string raw)
  {
    var parts = raw.Split(' ');
    return new(
      Name: parts[0],
      Path: parts[2],
      Type: parts[4],
      RawFlags: parts[5]
    );
  }
}