namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

public record FsStatDto(
  long Size,
  UnixFileMode Mode,
  int Uid,
  int Gid,
  double Atime,
  double Mtime,
  double Ctime,
  int Dev,
  int Inode,
  int Nlink,
  string User,
  string Group,
  bool Acl
);