namespace Org.Grush.NasFileCopy.Remote.Ui.DiTokens;

public record ReadWriteDirectory(
  DirectoryInfo Value
)
{
  public static implicit operator DirectoryInfo(ReadWriteDirectory dir) => dir.Value;
}