namespace Org.Grush.NasFileCopy.Remote.Share.Structures;

public record SystemInfoDto(
  string Version,
  string Hostname,
  ulong Physmem,
  string Model,
  int Cores,
  int Physical_cores,
  double Uptime_seconds
);