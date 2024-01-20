using System.CommandLine;
using Org.Grush.NasFileCopy.ServerSide.SystemCom;

namespace Org.Grush.NasFileCopy.ServerSide.Cli;

public class CopyCommand
{
  private readonly MountService _mountService;
  private readonly LsblkService _lsblkService;
  private readonly RsyncService _rsyncService;
  private readonly LockFileService _lockFileService;

  private Option<string> DestinationDeviceLabelOption { get; }
  private Option<string> SourceNameOption { get; }
  private Option<bool?> ForceKillOtherProcess { get; }

  public Command Command { get; }

  public CopyCommand(MountService mountService, LsblkService lsblkService, RsyncService rsyncService, LockFileService lockFileService)
  {
    _mountService = mountService;
    _lsblkService = lsblkService;
    _rsyncService = rsyncService;
    _lockFileService = lockFileService;

    DestinationDeviceLabelOption = new (
      name: "--destination-device-label",
      description: "The user-facing device label of the destination"
    );
    SourceNameOption = new (
      name: "--source-name",
      description: "Dataset name to copy from"
    );
    ForceKillOtherProcess = new(
      name: "--force-kill",
      description: "Kill existing processes. WARNING: May cause issues with transfers; use when stuck."
    );

    Command = new Command("copy", "Copy from one device to another")
    {
      DestinationDeviceLabelOption,
      SourceNameOption,
    };

    Command.SetHandler(Handle, DestinationDeviceLabelOption, SourceNameOption, ForceKillOtherProcess);
  }

  private async Task<int> Handle(string? destinationLabel, string? sourceName, bool? forceKill)
  {
    if (destinationLabel is null)
    {
      Console.WriteLine($"Missing {nameof(destinationLabel)}");
      return 1;
    }

    if (sourceName is null)
    {
      Console.WriteLine($"Missing {nameof(sourceName)}");
      return 1;
    }

    if (_lockFileService.CheckForLockedProcess(killIfExists: forceKill ?? false))
    {
      Console.WriteLine("A NasFileCopy process is already running");
      return 100;
    }

    var mountedSource = (await _mountService.ReadMounts())
      .FirstOrDefault(mnt => mnt.Name == sourceName);

    if (mountedSource is null)
    {
      Console.WriteLine($"Source dataset {sourceName} does not appear to be mounted");
      return 3;
    }

    if (!mountedSource.Path.StartsWith("/mnt/"))
    {
      Console.WriteLine($"Source mount point is not in /mnt/, likely incorrect: {mountedSource.Path}");
      return 4;
    }

    await _lsblkService.ReadLsblk();

    var destinationDevice = _lsblkService.Find(dev => dev.Label == destinationLabel && dev.Type == "partition").Single();

    string mountPoint;
    if (!destinationDevice.Mountpoints.Any(m => m is not null))
    {
      mountPoint = destinationDevice.Mountpoints.First(m => m is not null)!;
    }
    else
    {
      Console.WriteLine("Attempting to mount");
      var name = SanitizeName(destinationLabel);
      mountPoint = $"/mnt/{name}";

      var success = await _mountService.Mount(destinationLabel, mountPoint);
      if (!success)
      {
        Console.WriteLine($"Failed to mount {destinationLabel} to ${mountPoint}");
        return 2;
      }
    }

    // get a lock and make sure it's cleaned up afterwards
    using var handle = _lockFileService.CreateLock();

    var syncSuccess = await _rsyncService.Sync(mountedSource.Path, mountPoint);
    if (syncSuccess)
    {
      Console.WriteLine("Sync succeeded!");
    }
    return syncSuccess ? 255 : 0;
  }

  private static string SanitizeName(string name)
  {
    return string.Join("",
      name
        .Select(c =>
          char.IsAsciiLetterOrDigit(c)
            ? c.ToString()
            : ((int)c).ToString()
        )
    );
  }
}