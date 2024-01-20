using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.ServerSide.SystemCom;

public class LsblkService
{
  private const bool localTest = true;

  public LsblkOutput? Output { get; private set; }

  public async Task<bool> ReadLsblk()
  {
    var fullBlockOutput = await ReadLsblkWithArgs<LsblkOutputBP>("-b -p");
    var fullFilesystemOutput = await ReadLsblkWithArgs<LsblkOutputBPF>("-b -p -f");

    if (fullBlockOutput is null || fullFilesystemOutput is null)
      return false;

    Output = LsblkOutput.FromOutputs(fullBlockOutput, fullFilesystemOutput);
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

    if (localTest)
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

    return JsonSerializer.Deserialize<T>(outputString, new JsonSerializerOptions() {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
  }
}

public record LsblkOutput(
  IReadOnlyList<LsblkDevice> BlockDevices
)
{
  internal static LsblkOutput FromOutputs(LsblkOutputBP block, LsblkOutputBPF fs)
  {
    return new(
      BlockDevices:
        block.BlockDevices
          .Zip(fs.BlockDevices)
          .Select(pair => LsblkDevice.FromOutputs(pair.First, pair.Second))
          .ToList()
    );
  }
}

public record LsblkDevice(
  string Name,
  string MajMin,
  bool Rm,
  long Size,
  bool Ro,
  string Type,
  IReadOnlyList<string?> Mountpoints,
  string? Fstype,
  string? Fsver,
  string? Label,
  string? Uuid,
  IReadOnlyList<LsblkDevice>? Children
)
{
  internal static LsblkDevice FromOutputs(LsblkBlockDeviceBP block, LsblkFilesystemDeviceBPF fs)
  {
    IReadOnlyList<LsblkDevice>? children = null;
    if (block.Children is not null && fs.Children is not null)
    {
      children = block.Children
        .Zip(fs.Children)
        .Select(pair => LsblkDevice.FromOutputs(pair.First, pair.Second))
        .ToList();
    }

    return new(
      Name: block.Name,
      MajMin: block.MajMin,
      Rm: block.Rm,
      Size: block.Size,
      Ro: block.Ro,
      Type: block.Type,
      Mountpoints: block.Mountpoints,
      Fstype: fs.Fstype,
      Fsver: fs.Fsver,
      Label: fs.Label,
      Uuid: fs.Uuid,
      Children: children
    );
  }
}

public record LsblkOutputBP(
  [property:JsonPropertyName("blockdevices")]
  IReadOnlyList<LsblkBlockDeviceBP> BlockDevices
);

public interface ILsblkDeviceB<T>
{
  IReadOnlyList<T>? Children { get; }
}

/// <summary>
/// 
/// </summary>
/// <param name="Name">The device path name</param>
/// <param name="MajMin">"major:minor" device numbers</param>
/// <param name="Rm">Removable-device flag</param>
/// <param name="Size">Size in bytes</param>
/// <param name="Ro">Readonly-device flag</param>
/// <param name="Type">"disk" or "part"</param>
/// <param name="Mountpoints">??? Only seen this labelled for swap "[SWAP]"</param>
/// <param name="Children"></param>
public record LsblkBlockDeviceBP(
  string Name,
  [property:JsonPropertyName("maj:min")]
  string MajMin,
  bool Rm,
  long Size,
  bool Ro,
  string Type,
  IReadOnlyList<string?> Mountpoints,
  IReadOnlyList<LsblkBlockDeviceBP>? Children
) : ILsblkDeviceB<LsblkBlockDeviceBP>;

public record LsblkOutputBPF(
  [property:JsonPropertyName("blockdevices")]
  IReadOnlyList<LsblkFilesystemDeviceBPF> BlockDevices
);

/// <summary>
/// lsblk with -bpf the filesystem options.
/// </summary>
/// <param name="Name">The device path name</param>
/// <param name="Fstype">type, e.g. vfat, swap, zfs_member, exfat</param>
/// <param name="Fsver">version, e.g. FAT32, 1, 5000, 1.0</param>
/// <param name="Label">Optional user-facing partition label</param>
/// <param name="Uuid">fstype-specific identifier</param>
/// <param name="Mountpoints">??? Only seen this labelled for swap "[SWAP]"</param>
/// <param name="Children">For parent nodes, contains the child nodes</param>
public record LsblkFilesystemDeviceBPF(
  string Name,
  string? Fstype,
  string? Fsver,
  string? Label,
  string? Uuid,
  IReadOnlyList<string?> Mountpoints,
  IReadOnlyList<LsblkFilesystemDeviceBPF>? Children
) : ILsblkDeviceB<LsblkFilesystemDeviceBPF>;