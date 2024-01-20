using System.CommandLine;
using System.Text.Json;
using Org.Grush.NasFileCopy.ServerSide.SystemCom;

namespace Org.Grush.NasFileCopy.ServerSide.Cli;

public class ListCommand
{
  private Option<string?> LabelOption { get; }
  public Command Command { get; }
  
  public ListCommand()
  {
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
    var lsblkService = new LsblkService();

    await lsblkService.ReadLsblk();

    if (label is null)
    {
      var result = lsblkService.Find(dev => dev.Label == label).FirstOrDefault();
      if (result is null)
      {
        return 1;
      }
      Console.WriteLine(JsonSerializer.Serialize(result));
      return 0;
    }
    
    Console.WriteLine(JsonSerializer.Serialize(lsblkService.Output!));
    return 0;
  }
}