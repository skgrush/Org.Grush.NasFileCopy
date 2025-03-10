namespace Org.Grush.NasFileCopy.Remote.Ui.DiTokens;

public record ExeDirectory(
  DirectoryInfo Value
)
{
  public static implicit operator DirectoryInfo(ExeDirectory exeDirectory) => exeDirectory.Value;
}