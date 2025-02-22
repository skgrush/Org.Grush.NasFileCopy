namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

public record PoolDto(
  int Id,
  string Name,
  string Guid,
  int Encrypt,
  string EncryptKey,
  string Path,
  string Status,
  // object Scan,
  // object Topology,
  bool Healthy,
  string Status_detail,
  ValueDto<string> Autotrim,
  // object? Encryptkey_path,
  bool Is_decrypted
);