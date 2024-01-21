namespace Org.Grush.NasFileCopy.Structures;

public enum CopyCommandExitCodes : int
{
  OkOrHelp = 0,
  UncaughtException = 1,
  ArgumentOrCliIssue = 4,
  ProcessAlreadyRunning = 8,
  BadSourceDataSet = 12,
  BadDestinationLabel = 16,
  MountFailure = 24,
  SyncFailure = 32,
}