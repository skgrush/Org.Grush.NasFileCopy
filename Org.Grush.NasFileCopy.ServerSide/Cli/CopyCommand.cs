using System.CommandLine;
using Org.Grush.NasFileCopy.ServerSide.SystemCom;

namespace Org.Grush.NasFileCopy.ServerSide.Cli;

public class CopyCommand
{
  private readonly MountService _mountService;
  private readonly LsblkService _lsblkService;
  private readonly RsyncService _rsyncService;
  
  private Option<string> DestinationDeviceLabelOption { get; }
  private Option<string> SourceNameOption { get; }
  public Command Command { get; }

  public CopyCommand(MountService mountService, LsblkService lsblkService, RsyncService rsyncService)
  {
    _mountService = mountService;
    _lsblkService = lsblkService;
    _rsyncService = rsyncService;

    DestinationDeviceLabelOption = new Option<string>(
      name: "--destination-device-label",
      description: "The user-facing device label of the destination"
    );
    SourceNameOption = new Option<string>(
      name: "--source-name",
      description: "Dataset name to copy from"
    );
    Command = new Command("copy", "Copy from one device to another")
    {
      DestinationDeviceLabelOption,
      SourceNameOption,
    };
    
    Command.SetHandler(Handle, DestinationDeviceLabelOption, SourceNameOption);
  }

  private async Task<int> Handle(string? destinationLabel, string? sourceName)
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

    var mountedSource = (await _mountService.ReadMounts())
      .FirstOrDefault(mnt => mnt.Name == sourceName);

    if (mountedSource is null)
    {
      Console.WriteLine($"Source dataset {sourceName} does not appear to be mounted");
      return 3;
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

    var result = await _rsyncService.Sync(mountedSource.Path, mountPoint);
    return result ? 5 : 0;
  }

  private string SanitizeName(string name)
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