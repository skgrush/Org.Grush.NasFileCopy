using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisibleHidden
{
  HIDDEN = 0,
  VISIBLE = 1,
}