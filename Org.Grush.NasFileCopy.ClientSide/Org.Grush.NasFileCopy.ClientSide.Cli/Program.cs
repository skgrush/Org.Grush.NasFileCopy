// See https://aka.ms/new-console-template for more information

using Org.Grush.NasFileCopy.ClientSide.Cli;
using Org.Grush.NasFileCopy.ClientSide.Shared;
using Renci.SshNet;


Console.WriteLine("Hostname:");
var hostname = Console.ReadLine();

Console.WriteLine("Username:");
var username = Console.ReadLine();

Console.WriteLine("Password:");
var password = SecureReader.Read();
Console.WriteLine("");

Console.WriteLine("List or copy:");
var listOrCopy = Console.ReadLine()?.ToLower();
bool doList = false;
bool doCopy = false;
if ("list".StartsWith(listOrCopy))
  doList = true;
else if ("copy".StartsWith(listOrCopy))
  doCopy = true;
else
  throw new ArgumentException("Invalid input, must be 'list' or 'copy'");

var connectionInfo = new ConnectionInfo(hostname, username, new PasswordAuthenticationMethod(username, password));

var ssh = new NasComSshClient(connectionInfo, "/opt/");

var token = new CancellationToken();

if (doList)
{
  var result = await ssh.ListDevices(token);

  Console.WriteLine(result?.Serialize());
}
else if (doCopy)
{
  Console.WriteLine("Source mount point (should be full path, e.g. 'rootFiles/myFiles':");
  var source = Console.ReadLine();

  Console.WriteLine("Destination device label:");
  var deviceLabel = Console.ReadLine();
  var result = await ssh.Copy(token, source, deviceLabel);

  Console.WriteLine(result ? "Success" : "Failure");
}