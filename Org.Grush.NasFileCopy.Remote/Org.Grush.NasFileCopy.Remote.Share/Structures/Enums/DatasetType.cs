using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DatasetType
{
  FILESYSTEM = 1,
  VOLUME = 2,
}