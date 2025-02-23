using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures.Enums;


[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceType
{
  SERIAL = 1,
  DISK = 2,
}