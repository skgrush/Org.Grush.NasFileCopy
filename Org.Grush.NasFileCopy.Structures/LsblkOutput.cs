using System.Text.Json;
using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Structures;

public record LsblkOutput(
  int RecordVersion,
  IReadOnlyList<LsblkDevice> BlockDevices
)
{
  public string Serialize()
    => JsonSerializer.Serialize(this, LsblkDeviceContext.Default.LsblkOutput);

  public static LsblkOutput? Deserialize(string str)
    => JsonSerializer.Deserialize(str, LsblkDeviceContext.Default.LsblkOutput);
}

/// <summary>
/// Describes all output from lsblk calls with and without `-f`.
/// </summary>
/// <param name="Name">The device path name</param>
/// <param name="MajorDeviceNumber">Major device number</param>
/// <param name="MinorDeviceNumber">Minor device number</param>
/// <param name="Rm">Removable-device flag</param>
/// <param name="Size">Size in bytes</param>
/// <param name="Ro">Readonly-device flag</param>
/// <param name="Type">"disk" or "part"</param>
/// <param name="MountPoints">Filesystem mount point path, or special string such as "[SWAP]"</param>
/// <param name="FsType">type, e.g. vfat, swap, zfs_member, exfat</param>
/// <param name="FsVer">version, e.g. FAT32, 1, 5000, 1.0</param>
/// <param name="Label">Optional user-facing partition label</param>
/// <param name="Uuid">FsType-specific identifier</param>
/// <param name="Children">Child devices, or null on leaf nodes</param>
public record LsblkDevice(
  int RecordVersion,
  string Name,
  int MajorDeviceNumber,
  int MinorDeviceNumber,
  bool Rm,
  long Size,
  bool Ro,
  string Type,
  IReadOnlyList<string?> MountPoints,
  string? FsType,
  string? FsVer,
  string? Label,
  string? Uuid,
  IReadOnlyList<LsblkDevice>? Children
)
{
  public string Serialize()
    => JsonSerializer.Serialize(this, LsblkDeviceContext.Default.LsblkDevice);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LsblkDevice))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(IReadOnlyList<string?>))]
[JsonSerializable(typeof(IReadOnlyList<LsblkDevice>))]
[JsonSerializable(typeof(LsblkOutput))]
public partial class LsblkDeviceContext : JsonSerializerContext;