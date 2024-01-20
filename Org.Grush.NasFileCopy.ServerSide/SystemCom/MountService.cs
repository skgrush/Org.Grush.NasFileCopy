using System.Diagnostics;

namespace Org.Grush.NasFileCopy.ServerSide.SystemCom;

public class MountService
{
  public static readonly TimeSpan UnmountTimeout = new(0, 0, seconds: 3);

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

    if (!Directory.Exists(mountPoint))
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
    #if TARGET_MACOS
    process.StartInfo.WorkingDirectory = "/sbin";
    process.StartInfo.Arguments = $"-w LABEL='{label}' '{mountPoint}'";
    #else
    process.StartInfo.WorkingDirectory = "/bin";
    process.StartInfo.Arguments = $"-rw LABEL='{label}' '{mountPoint}'";
    #endif

    process.StartInfo.UseShellExecute = false;
    process.StartInfo.CreateNoWindow = true;
    process.Start();

    await process.WaitForExitAsync();

    return process.ExitCode is 0;
  }

  public async Task<bool> Unmount(string mountPoint)
  {
    if (!Directory.Exists(mountPoint))
    {
      Console.WriteLine($"mount point {mountPoint} doesn't exist, skipping unmount");
      return true;
    }

    if (mountPoint.Contains('\''))
    {
      Console.WriteLine($"Error: mountPoint is invalid due to apostrophe: {mountPoint}");
      return false;
    }


    while (await IsActiveMountPoint(mountPoint))
    {
      var success = await CallUmount(mountPoint);

      if (success)
      {
        Console.WriteLine("Successfully unmounted.");
        break;
      }

      Console.Write(".");
      await Task.Delay(UnmountTimeout);
    }

    return true;
  }

  private async Task<bool> CallUmount(string mountPoint)
  {
    var process = new Process();
    process.StartInfo.FileName = "umount";
    process.StartInfo.WorkingDirectory = "/usr/bin/";
    process.StartInfo.Arguments = $"'{mountPoint}'";

    process.StartInfo.UseShellExecute = false;
    process.StartInfo.CreateNoWindow = true;
    // process.StartInfo.RedirectStandardOutput = true;
    process.Start();

    // var outputString = await process.StandardOutput.ReadToEndAsync();

    await process.WaitForExitAsync();

    return process.ExitCode is 0;
  }

  private async Task<bool> IsActiveMountPoint(string mountPoint)
  {
    var process = new Process();
    process.StartInfo.FileName = "mountpoint";
    process.StartInfo.WorkingDirectory = "/usr/bin/";
    process.StartInfo.Arguments = $"'{mountPoint}'";

    process.StartInfo.UseShellExecute = false;
    process.StartInfo.CreateNoWindow = true;
    process.Start();

    await process.WaitForExitAsync();

    var code = process.ExitCode;

    return code is 0;
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
    #if TARGET_MACOS
    var parts = raw.Split(' ', 4, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    var type = parts[3].split(',', 2)[0][1..];
    return new(
      Name: parts[0],
      Path: parts[2],
      Type: type,
      RawFlags: parts[3]
    );

    #else
    var parts = raw.Split(' ', 6, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    return new(
      Name: parts[0],
      Path: parts[2],
      Type: parts[4],
      RawFlags: parts[5]
    );
    #endif
  }
}