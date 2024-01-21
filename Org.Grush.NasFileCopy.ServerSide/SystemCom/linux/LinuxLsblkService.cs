using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Org.Grush.NasFileCopy.ServerSide.SystemCom.linux;

public class LinuxLsblkService : LsblkService
{
  public override async Task<bool> ReadLsblk()
  {
    var fullBlockOutput = await ReadLsblkWithArgs("-b -p", LsblkOutputBp.Deserialize);
    var fullFilesystemOutput = await ReadLsblkWithArgs("-b -p -f", LsblkOutputBpf.Deserialize);

    if (fullBlockOutput is null || fullFilesystemOutput is null)
      return false;

    Output = CombineOutputs(fullBlockOutput, fullFilesystemOutput);
    return true;
  }

  private async Task<T?> ReadLsblkWithArgs<T>(string args, Func<string, T> deserialize)
  {
    var process = new Process();

    process.StartInfo.FileName = "lsblk";
    process.StartInfo.WorkingDirectory = "/bin";
    process.StartInfo.Arguments = $"--json {args}";

    process.StartInfo.UseShellExecute = false;
    process.StartInfo.CreateNoWindow = true;
    process.StartInfo.RedirectStandardOutput = true;
    process.Start();

    var outputString = await process.StandardOutput.ReadToEndAsync();

    await process.WaitForExitAsync();

    return deserialize(outputString);
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
}

[UsedImplicitly]
public record LsblkOutputBp(
  // ReSharper disable once StringLiteralTypo
  [property: JsonPropertyName("blockdevices")]
  IReadOnlyList<LsblkBlockDeviceBp> BlockDevices
)
{
  public static LsblkOutputBp Deserialize(string input)
    => JsonSerializer.Deserialize(input, LinuxLsblkContext.Default.LsblkOutputBp);
}

[UsedImplicitly]
public record LsblkBlockDeviceBp(
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
public record LsblkOutputBpf(
  // ReSharper disable once StringLiteralTypo
  [property: JsonPropertyName("blockdevices")]
  IReadOnlyList<LsblkFilesystemDeviceBpf> BlockDevices
)
{
  public static LsblkOutputBpf Deserialize(string input)
    => JsonSerializer.Deserialize(input, LinuxLsblkContext.Default.LsblkOutputBpf);
}

[UsedImplicitly]
[SuppressMessage("ReSharper", "StringLiteralTypo")]
public record LsblkFilesystemDeviceBpf(
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

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LsblkOutputBp))]
[JsonSerializable(typeof(LsblkOutputBpf))]
[JsonSerializable(typeof(LsblkBlockDeviceBp))]
[JsonSerializable(typeof(LsblkFilesystemDeviceBpf))]
[JsonSerializable(typeof(IReadOnlyList<LsblkBlockDeviceBp>))]
[JsonSerializable(typeof(IReadOnlyList<LsblkFilesystemDeviceBpf>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(IReadOnlyList<string?>))]
public partial class LinuxLsblkContext : JsonSerializerContext;