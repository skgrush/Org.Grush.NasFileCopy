using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Remote.Share.Ssh;


public record struct LsblkDevice(
  // device path e.g. '/dev/sda' or '/dev/sda1'
  string Path,
  // readonly
  bool Ro,
  // Hotpluggable
  bool Hotplug,
  // human-readable hardware manufacturer name, only applied to disks sometimes even if model is applied
  string? Vendor,
  // human-readable hardware device name, only applied to disks sometimes, even without vendor
  string? Model,
  // user-defined UI label
  string? Label,
  string Name,
  // special name for stuff like "Microsoft reserved partition"
  string? Partlabel,
  // the UUID of the partition type
  string? Parttype,
  // the human-readable name of the partition type
  string? Parttypename,
  // size in bytes; safely fits in JSON max safe int = 9_007_199_254_740_991 = 9 PB
  ulong Size,
  // null if unmounted
  // string path to mountpoint
  // square-bracketed string for virtual mounts like "[SWAP]"
  string? Mountpoint,
  // e.g. "disk" or "part" or "crypt"
  string Type,
  // transport, e.g. "usb" or "nvme", but can be null including for some USB partitions or virtuals
  string Tran,
  // filesystem type, e.g.  "vfat"  or  "zfs_member"  or  "swap"  or  "exfat"
  string Fstype,
  // file system, e.g.      "FAT32" or  "5000"        or  "1"     or  "1.0"
  string Fsver,
  ImmutableArray<string?> Mountpoints,
  ImmutableArray<LsblkDevice>? Children
)
{
  /// <summary>
  /// Print bytes as a number
  /// </summary>
  public const string Options = $"--bytes --paths --output {OutputColumns}";
  public const string OutputColumns =
    "PATH,RO,HOTPLUG,VENDOR,MODEL,LABEL,NAME,PARTLABEL,PARTTYPE,PARTTYPENAME,SIZE,MOUNTPOINT,TYPE,TRAN,FSTYPE,FSVER,MOUNTPOINTS";

  public record struct LsblkResult(
    ImmutableArray<LsblkDevice> Blockdevices
  );
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LsblkDevice))]
[JsonSerializable(typeof(ImmutableArray<string?>))]
[JsonSerializable(typeof(ImmutableArray<LsblkDevice>?))]
[JsonSerializable(typeof(LsblkDevice.LsblkResult))]
internal partial class LsblkDeviceJsonSerializerContext : JsonSerializerContext;