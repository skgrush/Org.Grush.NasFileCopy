using System.Text.Json;
using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Structures;

public record ListCommandAcceptableValues(
  IReadOnlyList<string> AcceptableDestinationLabels,
  IReadOnlyList<string> AcceptableSourceNames
)
{
  public string Serialize()
    => JsonSerializer.Serialize(this, ListCommandAcceptableValuesContext.Default.ListCommandAcceptableValues);

  public static ListCommandAcceptableValues? Deserialize(string str)
    => JsonSerializer.Deserialize(str, ListCommandAcceptableValuesContext.Default.ListCommandAcceptableValues);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ListCommandAcceptableValues))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(string))]
public partial class ListCommandAcceptableValuesContext : JsonSerializerContext;
