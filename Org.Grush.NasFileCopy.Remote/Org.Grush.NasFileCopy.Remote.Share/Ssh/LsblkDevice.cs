using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Remote.Share.Ssh;


/// <summary>
/// Entry in lsblk called with the args <see cref="Options"/>.
/// </summary>
/// <param name="Path">device path e.g. '/dev/sda' or '/dev/sda1'</param>
/// <param name="Ro">readonly</param>
/// <param name="Hotplug">Hotpluggable</param>
/// <param name="Vendor">human-readable hardware manufacturer name, only applied to disks sometimes even if model is applied</param>
/// <param name="Model">human-readable hardware device name, only applied to disks sometimes, even without vendor</param>
/// <param name="Label">user-defined UI label</param>
/// <param name="Name"></param>
/// <param name="Partlabel">special name for stuff like "Microsoft reserved partition"</param>
/// <param name="Parttype">the UUID of the partition type</param>
/// <param name="Parttypename">the human-readable name of the partition type</param>
/// <param name="Size">size in bytes; safely fits in JSON max safe int = 9_007_199_254_740_991 = 9 PB</param>
/// <param name="Mountpoint">
/// null if unmounted.
/// string path to mountpoint.
/// square-bracketed string for virtual mounts like "[SWAP]".
/// </param>
/// <param name="Type">e.g. "disk" or "part" or "crypt"</param>
/// <param name="Tran">transport, e.g. "usb" or "nvme", but can be null including for some USB partitions or virtuals</param>
/// <param name="Fstype">filesystem type, e.g.  "vfat"  or  "zfs_member"  or  "swap"  or  "exfat"</param>
/// <param name="Fsver"> file system, e.g.      "FAT32" or  "5000"        or  "1"     or  "1.0"</param>
/// <param name="Mountpoints">List of all current mountpoints. Will be <c>[null]</c> if no mountpoints.</param>
/// <param name="Children">All child devices of this device.</param>
public record struct LsblkDevice(
  string Path,
  bool Ro,
  bool Hotplug,
  string? Vendor,
  string? Model,
  string? Label,
  string Name,
  string? Partlabel,
  string? Parttype,
  string? Parttypename,
  string? Partuuid,
  ulong Size,
  string? Mountpoint,
  string Type,
  string Tran,
  string Fstype,
  string Fsver,
  ImmutableArray<string?> Mountpoints,
  ImmutableArray<LsblkDevice>? Children
)
{
  /// <summary>Print bytes as a number</summary>
  public const string Options = $"--bytes --json --paths --output {OutputColumns}";
  public const string OutputColumns =
    "PATH,RO,HOTPLUG,VENDOR,MODEL,LABEL,NAME,PARTLABEL,PARTTYPE,PARTTYPENAME,PARTUUID,SIZE,MOUNTPOINT,TYPE,TRAN,FSTYPE,FSVER,MOUNTPOINTS";

  public IEnumerable<LsblkDevice> DepthfirstRecurse()
  {
    yield return this;
    foreach (var child in Children ?? [])
    {
      foreach (var outItem in child.DepthfirstRecurse())
      {
        yield return outItem;
      }
    }
  }

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
