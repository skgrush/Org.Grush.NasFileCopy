using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(FsStatDto))]
[JsonSerializable(typeof(SystemInfoDto))]
[JsonSerializable(typeof(SshSettingsDto))]
[JsonSerializable(typeof(ImmutableArray<FsListdirDto>))]
[JsonSerializable(typeof(ImmutableArray<UserDto>))]
[JsonSerializable(typeof(ImmutableArray<PoolDatasetFilesystemDto>))]
[JsonSerializable(typeof(TrueNasHttpClient.FsListdirArg))]
[JsonSerializable(typeof(ImmutableDictionary<string, DeviceInfoDto>))]
public partial class HttpJsonSerializerContext : JsonSerializerContext;
