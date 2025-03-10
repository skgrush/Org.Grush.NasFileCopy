using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Org.Grush.NasFileCopy.Remote.Ui.DiTokens;

namespace Org.Grush.NasFileCopy.Remote.Ui.Services;

internal interface IStorageService
{
  public event EventHandler<(StorageConfig? oldConfig, StorageConfig newConfig)>? ConfigChanged;

  public StorageConfig ReadConfig();

  public void WriteConfig(StorageConfig value);
}



public record StorageConfig(
  string? Hostname = null,
  string? Username = null,
  string? PathToSshKey = null
)
{
  public static readonly StorageConfig Default = new();
}

internal class FileStorageService(
  ExeDirectory exeDirectoryToken
) : IStorageService
{
  private static readonly string ConfigFileName = typeof(FileStorageService).Assembly.FullName! + ".config.json";
  private readonly DirectoryInfo _exeDirectory = exeDirectoryToken;

  private FileInfo ConfigFile => new(Path.Combine(_exeDirectory.FullName, ConfigFileName));

  private StorageConfig? _config;

  public event EventHandler<(StorageConfig? oldConfig, StorageConfig newConfig)>? ConfigChanged;

  public StorageConfig ReadConfig()
  {
    if (_config is not null)
      return _config;

    var file = ConfigFile;
    if (!file.Exists)
      return StorageConfig.Default;

    using var fileStream = file.OpenRead();

    _config = JsonSerializer.Deserialize(fileStream, FileStorageConfigSerializerContext.Default.StorageConfig)!;

    ConfigChanged!.Invoke(this, (null, _config));

    return _config;
  }

  public void WriteConfig(StorageConfig value)
  {
    ArgumentNullException.ThrowIfNull(value);

    var existingConfig = ReadConfig();
    _config = value;

    if (existingConfig.Equals(_config))
      return;

    var file = ConfigFile;
    using var writeStream = file.Open(FileMode.Create, FileAccess.Write, FileShare.Read);

    JsonSerializer.Serialize(writeStream, _config, FileStorageConfigSerializerContext.Default.StorageConfig);

    ConfigChanged!.Invoke(this, (existingConfig, _config));
  }

}


[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StorageConfig))]
internal partial class FileStorageConfigSerializerContext : JsonSerializerContext;