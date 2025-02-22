namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

public record ValueDto<TParsed>(
  TParsed Parsed,
  string Rawvalue,
  string? Value,
  string Source
);