
namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

public record DeviceInfoDto(
  string Name,
  int Mediasize,
  int Sectorsize,
  int Stripesize,
  object Rotationrate,
  string Ident,
  string Lunid,
  string Descr,
  string Subsystem,
  int Number,
  string Model,
  string Type,
  int Size,
  int Blocks,
  string Bus
);