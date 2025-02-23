using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CaseSensitivity
{
  SENSITIVE = 1,
  INSENSITIVE = 2,
  MIXED = 3,
}