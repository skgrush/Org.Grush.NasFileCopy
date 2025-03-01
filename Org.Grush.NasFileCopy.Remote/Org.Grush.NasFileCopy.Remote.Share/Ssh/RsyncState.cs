namespace Org.Grush.NasFileCopy.Remote.Share.Ssh;

public record RsyncState(
  string LatestFile,

  ulong LatestFileByteProgress,
  byte LatestFilePercent,
  float LatestFileSpeed,
  TimeSpan LatestFileRemainingTime,

  ulong TotalXferCount,
  ulong ToCheckNumerator,
  ulong ToCheckDenominator
);