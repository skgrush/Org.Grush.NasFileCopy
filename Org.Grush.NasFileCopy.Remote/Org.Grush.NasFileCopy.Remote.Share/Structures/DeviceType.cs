using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures;


[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceType
{
  SERIAL = 1,
  DISK = 2,
}