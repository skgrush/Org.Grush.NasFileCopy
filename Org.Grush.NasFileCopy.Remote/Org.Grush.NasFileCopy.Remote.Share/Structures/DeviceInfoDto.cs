
namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

public record DeviceInfoDto(
  string Name,
  long Mediasize,
  int Sectorsize,
  int? Stripesize,
  object Rotationrate,
  string Ident,
  string Lunid,
  string Descr,
  string Subsystem,
  long Number,
  string Model,
  string Type,
  long Size,
  long Blocks,
  string Bus
);