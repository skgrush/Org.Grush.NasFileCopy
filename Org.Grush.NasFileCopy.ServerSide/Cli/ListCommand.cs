using System.CommandLine;
using Org.Grush.NasFileCopy.ServerSide.SystemCom;

namespace Org.Grush.NasFileCopy.ServerSide.Cli;

public class ListCommand
{
  private readonly LsblkService _lsblkService;
  private readonly MountService _mountService;

  private Option<string?> LabelOption { get; }
  public Command Command { get; }

  public ListCommand(LsblkService lsblkService, MountService mountService)
  {
    _lsblkService = lsblkService;
    _mountService = mountService;
    LabelOption = new Option<string?>(
      name: "--label",
      description: "The user-facing device label"
    );
    Command = new Command("list", "List devices")
    {
      LabelOption,
    };

    Command.SetHandler(Handle, LabelOption);
  }

  private async Task<int> Handle(string? label)
  {

    await _lsblkService.ReadLsblk();

    if (label is not null)
    {
      var result = _lsblkService.Find(dev => dev.Label == label).FirstOrDefault();
      if (result is null)
      {
        return 1;
      }
      Console.WriteLine(result.Serialize());
      return 0;
    }

    var viableSources = (await _mountService.ReadMounts())
      .Where(mnt => mnt.Path.StartsWith("/mnt/"))
      .Select(mnt => mnt.Name);

    var acceptableLabels = _lsblkService.Output!.BlockDevices.Where(dev => dev.Label is not null).Select(dev => dev.Label);
    Console.WriteLine("Acceptable destination labels:");
    Console.WriteLine(string.Join('\n', acceptableLabels));

    Console.WriteLine("Acceptable sources:");
    Console.WriteLine(string.Join('\n', viableSources));

    return 0;
  }
}