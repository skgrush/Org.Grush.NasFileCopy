using Org.Grush.NasFileCopy.Remote.Share.Structures.Enums;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

public record PoolDatasetCreateDto(
  string Name,
  DatasetType Type,
  long Volsize,
  /*enum*/string Volblocksize,
  bool Sparse,
  bool Force_size,
  string Comments,
  StandardAlwaysDisabled Sync,
  /*enum*/string Compression,
  OnOff Atime,
  OnOff Exec,
  string Managedby,
  long? Quota,
  long Quota_warning,
  long Quota_critical,
  long? Refquota,
  long Refquota_warning,
  long Refquota_critical,
  long Reservation,
  long Refreservation,
  long Special_small_block_size,
  long Copies,
  VisibleHidden Deduplication,
  /*enum*/string Checksum,
  OnOff Readonly,
  string Recordsize,
  CaseSensitivity Casesensitivity,
  /*enum*/string Aclmode,
  /*enum*/string Acltype,
  /*enum*/string Share_type,
  /*enum*/string Xattr,
  EncryptionOptions Encryption_options,
  bool Encryption,
  bool Inherit_encryption
);

public record EncryptionOptions(
  bool Generate_key,
  long Pbkdf2iters,
  /*enum*/string Algorithm,
  string? Passphrase,
  string? Key
);