// See https://aka.ms/new-console-template for more information

using System.CommandLine;
using Org.Grush.NasFileCopy.ServerSide.Cli;
using Org.Grush.NasFileCopy.ServerSide.SystemCom;
#if TARGET_MACOS
using Org.Grush.NasFileCopy.ServerSide.SystemCom.macos;
#else
using Org.Grush.NasFileCopy.ServerSide.SystemCom.linux;
#endif

#if TARGET_MACOS
var lsblkService = new MacosLsblkService()
#else
var lsblkService = new LinuxLsblkService();
#endif

var mountService = new MountService();
var rsyncService = new RsyncService();
var lockFileService = new LockFileService();

var rootCommand = new RootCommand("Server-side NasFileCopy CLI");

rootCommand.AddCommand(new ListCommand(lsblkService).Command);
rootCommand.AddCommand(new CopyCommand(mountService, lsblkService, rsyncService, lockFileService).Command);

return await rootCommand.InvokeAsync(args);
