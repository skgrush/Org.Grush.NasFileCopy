using System.Collections.Immutable;

namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

public record UserDto(
  uint Uid,
  string username,
  string Home,
  string Shell,
  string Full_name,
  bool Builtin,
  bool Smb,
  bool Password_disabled,
  bool Ssh_password_enabled,
  bool Locked,
  ImmutableArray<string> Sudo_commands,
  ImmutableArray<string> Sudo_commands_nopasswd,
  string? Email,
  GroupDto Group,
  ImmutableArray<uint> Groups,
  string? Sshpubkey,
  bool Immutable,
  bool Twofactor_auth_configured,
  bool Local,
  bool Id_type_both,
  string? Nt_name
  // object? sid
);

public record GroupDto(
  uint Bsdgrp_gid,
  string Bsdgrp_group,
  bool Bsdgrp_builtin,
  ImmutableArray<string> Bsdgrp_sudo_commands,
  ImmutableArray<string> Bsdgrp_sudo_commands_nopasswd,
  bool Bsdgrp_smb
);