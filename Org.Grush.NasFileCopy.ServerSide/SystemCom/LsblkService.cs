
using Org.Grush.NasFileCopy.Structures;

namespace Org.Grush.NasFileCopy.ServerSide.SystemCom;

public abstract class LsblkService
{
  public LsblkOutput? Output { get; protected set; }

  public abstract Task<bool> ReadLsblk();

  public IEnumerable<LsblkDevice> Find(Func<LsblkDevice, bool> predicate)
  {
    if (Output is null)
      throw new InvalidOperationException($"Call to {nameof(Find)}() before {nameof(ReadLsblk)}()");

    return Output.BlockDevices
      .SelectMany(item => item.Children is null ? new[] { item } : item.Children.Prepend(item))
      .Where(predicate);
  }
}
