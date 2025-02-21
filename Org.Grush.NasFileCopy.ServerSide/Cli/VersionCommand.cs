using System.CommandLine;
using System.Reflection;

namespace Org.Grush.NasFileCopy.ServerSide.Cli;

public class VersionCommand
{
  private static readonly string? Version = Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

  public Command Command { get; }

  public VersionCommand()
  {
    Command = new Command("version", "Get the informational version of the program.");

    Command.SetHandler(Handle);
  }

  private void Handle()
  {
    Console.WriteLine(Version);
  }
}