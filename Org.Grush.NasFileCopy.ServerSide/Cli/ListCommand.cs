using System.CommandLine;
using System.Text.Json;
using Org.Grush.NasFileCopy.ServerSide.Config;
using Org.Grush.NasFileCopy.ServerSide.SystemCom;

namespace Org.Grush.NasFileCopy.ServerSide.Cli;

public class ListCommand
{
  private readonly LsblkService _lsblkService;

  private Option<string?> LabelOption { get; }
  public Command Command { get; }

  public ListCommand(LsblkService lsblkService)
  {
    _lsblkService = lsblkService;
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
      Console.WriteLine(JsonSerializer.Serialize(result, JsonSettings.Options));
      return 0;
    }

    Console.WriteLine(JsonSerializer.Serialize(_lsblkService.Output!, JsonSettings.Options));
    return 0;
  }
}