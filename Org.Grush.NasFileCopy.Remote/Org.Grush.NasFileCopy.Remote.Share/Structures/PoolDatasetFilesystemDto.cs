using System.Collections.Immutable;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

//  parsed    value
//=================
//  null      null
//  null      "HIDDEN"
//  "none"    null
//  "off"     null
//  "0"       "0"
//  1         "1"
//  131072    "128K"

public record PoolDatasetFilesystemDto(
  string Id,
  string Type,
  ImmutableArray<PoolDatasetFilesystemDto> Children,
  string Name,
  string Pool,
  bool Encrypted,
  string? Encryption_root,
  bool Key_loaded,
  string Mountpoint,
  ValueDto<string>? Comments,
  ValueDto<string>? ManagedBy,
  ValueDto<string> Deduplication,
  ValueDto<string> Aclmode,
  ValueDto<string> Acltype,
  ValueDto<bool?> Xattr,
  ValueDto<bool> Atime,
  ValueDto<string> Casesensitivity,
  // ValueDto<bool> Checksum,
  // ValueDto<bool> Exec,
  // ValueDto<string> Sync,
  // ValueDto<string> Compression,
  // ValueDto<string> Compressratio,
  // ValueDto<string> Origin,
  ValueDto<bool> Readonly,
  ValueDto<string> Encryption_algorithm,
  ValueDto<long> Used,
  ValueDto<long> Available,
  bool Locked
)
{
  public IEnumerable<PoolDatasetFilesystemDto> DepthfirstRecurse()
  {
    yield return this;
    foreach (var child in Children)
    {
      foreach (var outItem in child.DepthfirstRecurse())
      {
        yield return outItem;
      }
    }
  }
}

