using System.CommandLine;
using Org.Grush.NasFileCopy.ServerSide.SystemCom;
using Org.Grush.NasFileCopy.Structures;

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

  private async Task<CopyCommandExitCodes> Handle(string? destinationLabel, string? sourceName, bool? forceKill)
  {
    if (destinationLabel is null)
    {
      Console.WriteLine($"Missing {nameof(destinationLabel)}");
      return CopyCommandExitCodes.OkOrHelp;
    }

    if (sourceName is null)
    {
      Console.WriteLine($"Missing {nameof(sourceName)}");
      return CopyCommandExitCodes.ArgumentOrCliIssue;
    }

    if (_lockFileService.CheckForLockedProcess(killIfExists: forceKill ?? false))
    {
      Console.WriteLine("A NasFileCopy process is already running");
      return CopyCommandExitCodes.ProcessAlreadyRunning;
    }

    var viableSources = (await _mountService.ReadMounts())
      .Where(mnt => mnt.Path.StartsWith("/mnt/"))
      .ToList();

    var mountedSource = viableSources.FirstOrDefault(mnt => mnt.Name == sourceName);

    if (mountedSource is null)
    {
      Console.WriteLine($"Source dataset {sourceName} does not appear to be mounted or not mounted in /mnt/");
      var opts = string.Join(", ", viableSources.Select(mnt => mnt.Name));
      Console.WriteLine($"Acceptable sources: {opts}");
      return CopyCommandExitCodes.BadSourceDataSet;
    }

    await _lsblkService.ReadLsblk();

    var destinationDevice = _lsblkService.Find(dev => dev.Label == destinationLabel && dev is { Type: "part" }).FirstOrDefault();

    if (destinationDevice is null)
    {
      Console.WriteLine($"Failed to find destination partition named {destinationLabel}");
      var viableOptions = _lsblkService.Find(dev => dev is { Type: "part" }).ToList();

      Console.WriteLine("Acceptable destination labels:");
      foreach (var dev in viableOptions)
      {
        Console.WriteLine($"  '{dev.Label}' : size={dev.Size:N}");
      }
      return CopyCommandExitCodes.BadDestinationLabel;
    }

    // get a lock and make sure it's cleaned up afterwards
    using var handle = _lockFileService.CreateLock();

    string destinationMountPoint;
    if (destinationDevice.MountPoints.Any(m => m is not null))
    {
      destinationMountPoint = destinationDevice.MountPoints.First(m => m is not null)!;
    }
    else
    {
      Console.WriteLine("Attempting to mount");
      var name = SanitizeName(destinationLabel);
      destinationMountPoint = $"/mnt/{name}";

      var success = await _mountService.Mount(destinationLabel, destinationMountPoint);
      if (!success)
      {
        Console.WriteLine($"Failed to mount {destinationLabel} to {destinationMountPoint}");
        return CopyCommandExitCodes.MountFailure;
      }
    }

    var syncSuccess = await _rsyncService.Sync(mountedSource.Path, destinationMountPoint);
    if (syncSuccess)
    {
      Console.WriteLine("Sync succeeded!");
    }

    // Failed unmount is not a critical issue
    var unmountSuccess = await _mountService.Unmount(destinationMountPoint);

    return syncSuccess ? CopyCommandExitCodes.OkOrHelp : CopyCommandExitCodes.SyncFailure;
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