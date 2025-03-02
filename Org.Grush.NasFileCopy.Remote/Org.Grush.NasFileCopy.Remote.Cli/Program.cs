// See https://aka.ms/new-console-template for more information

using System.Collections.Immutable;
using System.Text.Json;
using Org.Grush.NasFileCopy.Remote.Cli;
using Org.Grush.NasFileCopy.Remote.Share;

Console.Write("Hostname: ");
var hostname = Console.ReadLine() ?? throw new ArgumentNullException("Hostname");

Console.Write("Username: ");
var username = Console.ReadLine() ?? throw new ArgumentNullException("Username");

Console.Write("Password: ");
var password = SecureReader.Read();
Console.WriteLine();

var cancellationToken = CancellationToken.None;

await using var client = new TrueNasHttpClient(hostname, username, password);

await client.ConnectAsync(cancellationToken);

var cmds = client.ApiMethods
  .ToImmutableDictionary(
    c => c.Method.Name[..^5]
  );

while (true)
{
  Console.Write("Enter a command ({0}): ", string.Join(',', cmds.Keys));
  var requestedCommand = Console.ReadLine();

  if (!cmds.TryGetValue(requestedCommand, out var command))
  {
    Console.WriteLine("Command not found");
    continue;
  }

  var parameters = command.Method.GetParameters()[..^1];
  List<object> cmdArgs = [];
  foreach (var param in parameters)
  {
    Console.Write("Enter a '{0}': ", param.Name);
    var arg = Console.ReadLine();
    Console.WriteLine(param.ParameterType.Name);

    if (param.ParameterType == typeof(string))
      cmdArgs.Add(arg);
    else if (param.ParameterType.IsEnum)
      cmdArgs.Add(JsonSerializer.Deserialize('"'+arg+'"', returnType: param.ParameterType)!);
    else
      cmdArgs.Add(JsonSerializer.Deserialize(arg!, returnType: param.ParameterType)!);
  }
  cmdArgs.Add(cancellationToken);

  try
  {
    var resultTask = command.DynamicInvoke(cmdArgs.ToArray());

    await (Task)resultTask;

    var result = resultTask.GetType().GetProperty("Result")!.GetValue(resultTask);

    Console.WriteLine(JsonSerializer.Serialize(
      value: result,
      inputType: result.GetType(),
      new JsonSerializerOptions
      {
        WriteIndented = true,
      })
    );
  }
  catch (Exception ex)
  {
    Console.WriteLine(ex.Message);
    Console.WriteLine("\n");
  }
}