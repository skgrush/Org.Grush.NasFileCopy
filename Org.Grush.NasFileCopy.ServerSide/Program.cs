// See https://aka.ms/new-console-template for more information

using System.CommandLine;
using System.Reflection;
using Org.Grush.NasFileCopy.ServerSide.Cli;
using Org.Grush.NasFileCopy.ServerSide.SystemCom;
using Org.Grush.NasFileCopy.ServerSide.SystemCom.linux;

var version = Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

var lsblkService = new LinuxLsblkService();

var mountService = new MountService();
var rsyncService = new RsyncService();
var lockFileService = new LockFileService();

var rootCommand = new RootCommand("Server-side NasFileCopy CLI");

rootCommand.AddCommand(new ListCommand(lsblkService, mountService).Command);
rootCommand.AddCommand(new CopyCommand(mountService, lsblkService, rsyncService, lockFileService).Command);
rootCommand.AddCommand(new VersionCommand().Command);

return await rootCommand.InvokeAsync(args);
