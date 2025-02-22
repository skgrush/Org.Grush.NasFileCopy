namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

public record FsListdirDto(
  string Name,
  string Path,
  string Realpath,
  string Type,
  long Size,
  UnixFileMode Mode,
  bool Acl,
  int Uid,
  int Gid
);