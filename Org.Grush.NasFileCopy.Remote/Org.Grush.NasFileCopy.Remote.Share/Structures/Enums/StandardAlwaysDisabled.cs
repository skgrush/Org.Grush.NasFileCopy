using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StandardAlwaysDisabled
{
  DISABLED = 0,
  STANDARD = 1,
  ALWAYS = 2,
}