using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OnOff
{
  OFF = 0,
  ON = 1,
}