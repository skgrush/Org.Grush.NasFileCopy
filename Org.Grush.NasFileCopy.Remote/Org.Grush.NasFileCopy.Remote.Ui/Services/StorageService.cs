using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Org.Grush.NasFileCopy.Remote.Ui.DiTokens;

namespace Org.Grush.NasFileCopy.Remote.Ui.Services;

public interface IStorageService
{
  public event EventHandler<(StorageConfig? oldConfig, StorageConfig newConfig)>? ConfigChanged;

  public StorageConfig ReadConfig();

  public void WriteConfig(Func<StorageConfig, StorageConfig> updateFn);
}



public record StorageConfig(
  string? Hostname = null,
  string? Username = null,
  string? PrivateKey = null,
  bool PrivateKeyHasPassphrase = false
)
{
  public static readonly StorageConfig Default = new();
}

internal class FileStorageService(
  ReadWriteDirectory rwDirToken
) : IStorageService
{
  private static readonly string ConfigFileName = typeof(FileStorageService).Assembly.FullName! + ".config.json";
  private readonly DirectoryInfo _rwDir = rwDirToken;

  private FileInfo ConfigFile => new(Path.Combine(_rwDir.FullName, ConfigFileName));

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

  public void WriteConfig(Func<StorageConfig, StorageConfig> updateFn)
  {
    ArgumentNullException.ThrowIfNull(updateFn);

    var existingConfig = ReadConfig();
    _config = updateFn(existingConfig);

    if (existingConfig.Equals(_config))
      return;

    var file = ConfigFile;
    try
    {
      using var writeStream = file.Open(FileMode.Create, FileAccess.Write, FileShare.Read);

      JsonSerializer.Serialize(writeStream, _config, FileStorageConfigSerializerContext.Default.StorageConfig);
    }
    catch (Exception ex)
    {
      Console.WriteLine(ex);
    }
    ConfigChanged!.Invoke(this, (existingConfig, _config));
  }

}


[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StorageConfig))]
internal partial class FileStorageConfigSerializerContext : JsonSerializerContext;