using System.Collections.Immutable;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

public record SshSettingsDto(
  uint Id,
  ImmutableArray<object> Bindiface,
  ushort Tcpport,
  ImmutableArray<string> Password_login_groups,
  bool Passwordauth,
  bool Kerberosauth,
  bool Tcpfwd,
  bool Compression,
  string Privatekey,
  string Sftp_log_level,
  string Sftp_log_facility,
  string? Host_dsa_key,
  string? Host_dsa_key_pub,
  string? Host_dsa_key_cert_pub,
  string? Host_ecdsa_key,
  string? Host_ecdsa_key_pub,
  string? Host_ecdsa_key_cert_pub,
  string? Host_ed25519_key,
  string? Host_ed25519_key_pub,
  string? Host_ed25519_key_cert_pub,
  string? Host_key,
  string? Host_key_pub,
  string? Host_rsa_key,
  string? Host_rsa_key_pub,
  string? Host_rsa_key_cert_pub,
  ImmutableArray<string> Weak_ciphers,
  string Options
);