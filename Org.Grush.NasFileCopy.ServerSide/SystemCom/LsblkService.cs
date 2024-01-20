
namespace Org.Grush.NasFileCopy.ServerSide.SystemCom;

public abstract class LsblkService
{
  public LsblkOutput? Output { get; protected set; }

  public abstract Task<bool> ReadLsblk();

  public IEnumerable<LsblkDevice> Find(Func<LsblkDevice, bool> predicate)
  {
    if (Output is null)
      throw new InvalidOperationException($"Call to {nameof(Find)}() before {nameof(ReadLsblk)}()");

    return Output.BlockDevices
      .SelectMany(item => item.Children is null ? new[] { item } : item.Children.Prepend(item))
      .Where(predicate);
  }
}

public record LsblkOutput(
  IReadOnlyList<LsblkDevice> BlockDevices
);

// ReSharper disable NotAccessedPositionalProperty.Global

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
);