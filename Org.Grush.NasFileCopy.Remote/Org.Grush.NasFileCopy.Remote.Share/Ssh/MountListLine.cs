using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Org.Grush.NasFileCopy.Remote.Share.Ssh;

public record MountListLine(
  string Device,
  string MountPoint,
  ImmutableHashSet<string> Flags
)
{
  public static readonly Regex Re = new("""
                                        ^(?<device>.*?)
                                        \ on\ (?<path>.*?)
                                        (\ type\ (?<type>.*))? # some IMPLs list type inline, some have it as the first flag
                                        \ \((?<flags>.*)\) # some IMPLs separate flags by comma and space, some omit space
                                        $
                                        """, RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace);

  public static IEnumerable<MountListLine> ReadLines(string lines)
    => Re.Matches(lines)
      .Select(v =>
        new MountListLine(
          Device: v.Groups["device"].Value,
          MountPoint: v.Groups["path"].Value,
          Flags: ReadFlags(v.Groups["flags"].Value)
        )
      );

  private static ImmutableHashSet<string> ReadFlags(string flags)
    => flags.Split(',', StringSplitOptions.RemoveEmptyEntries)
      .Select(v => v.Trim())
      .ToImmutableHashSet();
}