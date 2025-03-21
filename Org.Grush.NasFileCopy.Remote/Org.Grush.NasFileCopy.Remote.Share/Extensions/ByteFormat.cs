namespace Org.Grush.NasFileCopy.Remote.Share.Extensions;

public static class ByteFormat
{
  private static readonly IReadOnlyList<string> SizeSuffixes =
    [ "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" ];
  private static readonly int LastSizeSuffixIdx = SizeSuffixes.Count - 1;

  public static string FormatToBytes(this ulong b)
  {
    int mag = Math.Min(
      (int)Math.Log(b, 1000),
      LastSizeSuffixIdx
    );

    string suffix = SizeSuffixes[mag];

    return $"{b / Math.Pow(1000, mag):g4} {suffix}";
  }
}