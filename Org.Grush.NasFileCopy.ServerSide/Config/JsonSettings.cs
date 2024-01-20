using System.Text.Json;

namespace Org.Grush.NasFileCopy.ServerSide.Config;

public static class JsonSettings
{
  public static readonly JsonSerializerOptions Options = new ()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
  };
}