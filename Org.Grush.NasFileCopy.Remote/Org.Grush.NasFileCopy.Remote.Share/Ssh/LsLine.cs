using System.Text.RegularExpressions;

namespace Org.Grush.NasFileCopy.Remote.Share.Ssh;

public enum LsType
{
  RegularFile = '-',
  BlockSpecialFile = 'b',
  CharacterSpecialFile = 'c',
  Directory = 'd',
  SymbolicLink = 'l',
  Fifo = 'p',
  Socket = 's',
  Whiteout = 'w',
}

public record LsLine(
  LsType FileType,
  UnixFileMode FileMode,
  string AclFlag,
  string _UnknownNumber,
  string User,
  string Group,
  ulong Size,
  DateTime? Timestamp,
  string Name
)
{
  public const string LsFlags = "-al -D \"%FT%T\"";

  private static readonly Regex Re = new("""
    ^
    (?<FileType>.)
    (?<FileMode>[-rwoSsxTt]{9})
    (?<AclFlag>\S)?
    \s+
    (?<unknownNumber>\d+)
    \s+
    (?<user>\S+)
    \s+
    (?<group>\S+)
    \s+
    (?<size>\d+)
    \s+
    (?<timestamp>[\dT\-\:]+)
    \s+
    (?<name>.*)
    $
    """,
    RegexOptions.IgnorePatternWhitespace
  );

  public static IEnumerable<LsLine> Parse(string lines)
    => lines
      .Trim('\n')
      .Split('\n')
      .Select(line => Re.Match(line))
      .Where(match => match.Success)
      .Select(match => new LsLine(
        FileType: (LsType)match.Groups["FileType"].Value[0],
        FileMode: ParseMode(match.Groups["FileMode"].Value),
        AclFlag: match.Groups["AclFlag"].Value,
        _UnknownNumber: match.Groups["unknownNumber"].Value,
        User: match.Groups["user"].Value,
        Group: match.Groups["group"].Value,
        Size: ulong.Parse(match.Groups["size"].Value),
        Timestamp: DateTime.TryParse(match.Groups["timestamp"].Value, out var dt) ? dt : null,
        Name: match.Groups["name"].Value
      ));

  // public static UnixFileMode ParseMode(string mode)
  // {
  //   string uperm = mode[0..3];
  //   string gperm = mode[3..6];
  //   string operm = mode[6..9];
  //
  //   UnixFileMode result = UnixFileMode.None;
  //   foreach (var (permStr, i) in ((string[]) [operm, gperm, uperm]).Select((v, i) => (v, i)))
  //   {
  //     UnixFileMode unoffsetPerms = UnixFileMode.None;
  //     if (permStr[0] is 'r')
  //       unoffsetPerms |= UnixFileMode.OtherRead;
  //     if (permStr[1] is 'w')
  //       unoffsetPerms |= UnixFileMode.OtherWrite;
  //     if (permStr[2] is 'x')
  //       unoffsetPerms |= UnixFileMode.OtherExecute;
  //
  //     result |= (UnixFileMode)((int)unoffsetPerms << (4 * i));
  //
  //     if (i is 0 && permStr[2] is 'T' or 't')
  //     {
  //       result |= UnixFileMode.StickyBit;
  //       if (permStr[2] is 't')
  //         result |= UnixFileMode.OtherExecute;
  //     }
  //     else if (permStr[2] is 'S' or 's')
  //     {
  //       if (i is 2)
  //         result |= UnixFileMode.SetUser;
  //       else
  //         result |= UnixFileMode.SetGroup;
  //
  //       if (permStr[2] is 's')
  //         result |= (UnixFileMode)((int)UnixFileMode.OtherExecute << (4 * i));
  //     }
  //   }
  //
  //   return result;
  // }

  public static UnixFileMode ParseMode(string mode)
  {
    UnixFileMode result = UnixFileMode.None;

    if (mode[0] is 'r')
      result |= UnixFileMode.UserRead;
    if (mode[1] is 'w')
      result |= UnixFileMode.UserWrite;
    if (mode[2] is 'x' or 's')
      result |= UnixFileMode.UserExecute;
    if (mode[2] is 'S' or 's')
      result |= UnixFileMode.SetUser;

    if (mode[3] is 'r')
      result |= UnixFileMode.GroupRead;
    if (mode[4] is 'w')
      result |= UnixFileMode.GroupWrite;
    if (mode[5] is 'x' or 's')
      result |= UnixFileMode.GroupExecute;
    if (mode[5] is 'S' or 's')
      result |= UnixFileMode.SetGroup;

    if (mode[6] is 'r')
      result |= UnixFileMode.OtherRead;
    if (mode[7] is 'w')
      result |= UnixFileMode.OtherWrite;
    if (mode[8] is 'x' or 't')
      result |= UnixFileMode.OtherExecute;
    if (mode[8] is 'T' or 't')
      result |= UnixFileMode.StickyBit;

    return result;
  }
}