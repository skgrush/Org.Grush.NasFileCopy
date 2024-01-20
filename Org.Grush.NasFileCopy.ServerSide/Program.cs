// See https://aka.ms/new-console-template for more information

using System.CommandLine;
using Org.Grush.NasFileCopy.ServerSide.Cli;

var rootCommand = new RootCommand("Server-side NasFileCopy CLI");

var listCommand = new ListCommand();

rootCommand.AddCommand(listCommand.Command);

return await rootCommand.InvokeAsync(args);