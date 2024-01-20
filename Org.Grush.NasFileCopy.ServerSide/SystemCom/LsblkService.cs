using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using Org.Grush.NasFileCopy.ServerSide.Config;

namespace Org.Grush.NasFileCopy.ServerSide.SystemCom;

public class LsblkService
{
  private const bool LocalTest = true;

  public LsblkOutput? Output { get; private set; }

  public async Task<bool> ReadLsblk()
  {
    var fullBlockOutput = await ReadLsblkWithArgs<LsblkOutputBp>("-b -p");
    var fullFilesystemOutput = await ReadLsblkWithArgs<LsblkOutputBpf>("-b -p -f");

    if (fullBlockOutput is null || fullFilesystemOutput is null)
      return false;

    Output = CombineOutputs(fullBlockOutput, fullFilesystemOutput);
    return true;
  }

  public IEnumerable<LsblkDevice> Find(Func<LsblkDevice, bool> predicate)
  {
    if (Output is null)
      throw new InvalidOperationException($"Call to {nameof(Find)}() before {nameof(ReadLsblk)}()");

    return Output.BlockDevices
      .SelectMany(item => item.Children is null ? new[] { item } : item.Children.Prepend(item))
      .Where(predicate);
  }

  private async Task<T?> ReadLsblkWithArgs<T>(string args)
  {
    var process = new Process();

    if (LocalTest)
    {
      process.StartInfo.WorkingDirectory = "/bin";
      process.StartInfo.FileName = "bash";
      process.StartInfo.Arguments =
        args.Contains("-f")
          ? $"/Users/samuel/repos/Org.Grush.NasFileCopy/lsblk-fs --json {args}"
          : $"/Users/samuel/repos/Org.Grush.NasFileCopy/lsblk --json {args}";
    }
    else
    {
      process.StartInfo.FileName = "lsblk";
      process.StartInfo.WorkingDirectory = "/bin";
      process.StartInfo.Arguments = args;
    }

    process.StartInfo.UseShellExecute = false;
    process.StartInfo.CreateNoWindow = true;
    process.StartInfo.RedirectStandardOutput = true;
    process.Start();

    var outputString = await process.StandardOutput.ReadToEndAsync();

    await process.WaitForExitAsync();

    return JsonSerializer.Deserialize<T>(outputString, JsonSettings.Options);
  }

  private static LsblkOutput CombineOutputs(LsblkOutputBp block, LsblkOutputBpf fs)
  {
    return new(
      BlockDevices:
      block.BlockDevices
        .Zip(fs.BlockDevices)
        .Select(pair => CombineOutputs(pair.First, pair.Second))
        .ToList()
    );
  }

  private static LsblkDevice CombineOutputs(LsblkBlockDeviceBp block, LsblkFilesystemDeviceBpf fs)
  {
    IReadOnlyList<LsblkDevice>? children = null;
    if (block.Children is not null && fs.Children is not null)
    {
      children = block.Children
        .Zip(fs.Children)
        .Select(pair => CombineOutputs(pair.First, pair.Second))
        .ToList();
    }

    var majMinParts = block.MajMin.Split(':').Select(int.Parse).ToList();
    if (majMinParts.Count is not 2)
      throw new InvalidOperationException($"MajMin must be number:number, got {block.MajMin}");

    return new(
      Name: block.Name,
      MajorDeviceNumber: majMinParts[0],
      MinorDeviceNumber: majMinParts[1],
      Rm: block.Rm,
      Size: block.Size,
      Ro: block.Ro,
      Type: block.Type,
      MountPoints: block.MountPoints,
      FsType: fs.FsType,
      FsVer: fs.FsVer,
      Label: fs.Label,
      Uuid: fs.Uuid,
      Children: children
    );
  }

  [UsedImplicitly]
  private record LsblkOutputBp(
    // ReSharper disable once StringLiteralTypo
    [property: JsonPropertyName("blockdevices")]
    IReadOnlyList<LsblkBlockDeviceBp> BlockDevices
  );

  [UsedImplicitly]
  private record LsblkBlockDeviceBp(
    string Name,
    [property: JsonPropertyName("maj:min")]
    string MajMin,
    bool Rm,
    long Size,
    bool Ro,
    string Type,
    // ReSharper disable once StringLiteralTypo
    [property: JsonPropertyName("mountpoints")]
    IReadOnlyList<string?> MountPoints,
    IReadOnlyList<LsblkBlockDeviceBp>? Children
  );

  [UsedImplicitly]
  private record LsblkOutputBpf(
    // ReSharper disable once StringLiteralTypo
    [property: JsonPropertyName("blockdevices")]
    IReadOnlyList<LsblkFilesystemDeviceBpf> BlockDevices
  );

  [UsedImplicitly]
  [SuppressMessage("ReSharper", "StringLiteralTypo")]
  private record LsblkFilesystemDeviceBpf(
    string Name,
    [property: JsonPropertyName("fstype")]
    string? FsType,
    [property: JsonPropertyName("fsver")]
    string? FsVer,
    string? Label,
    string? Uuid,
    [property: JsonPropertyName("mountpoints")]
    IReadOnlyList<string?> MountPoints,
    IReadOnlyList<LsblkFilesystemDeviceBpf>? Children
  );
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