using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Org.Grush.NasFileCopy.ServerSide.SystemCom;

public class LockFileService
{
  public const string LockPath = "/run/lock/LCK.NasFileCopy";

  /// <summary>
  /// Check if an existing NasFileCopy instance exists.
  /// </summary>
  /// <param name="killIfExists">If true and a process exists, kill it.</param>
  /// <returns>true if a locked process still exists.</returns>
  public bool CheckForLockedProcess(bool killIfExists = false)
  {
    var proc = GetLockProcess();

    if (proc is null)
      return false;

    if (!killIfExists)
      return true;

    proc.Kill(true);
    DeleteLockFile();
    return false;
  }

  public LockFileHandle CreateLock()
  {
    var myProcess = Process.GetCurrentProcess();
    var contents = new LockFileContents(
      Pid: myProcess.Id
    ).Serialize();

    if (LockFileExists())
      throw new InvalidOperationException($"Call to {nameof(CreateLock)} while lock file exists");

    using var file = new StreamWriter(LockPath, Encoding.UTF8, new ()
    {
      Mode = FileMode.CreateNew,
      Access = FileAccess.Write,
      UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
    });

    file.WriteLine(contents);

    return new LockFileHandle(LockPath);
  }

  private Process? GetLockProcess()
  {
    var lockFileContents = ReadLockFile();
    if (lockFileContents is null)
      return null;

    var pid = lockFileContents.Pid;

    try
    {
      return Process.GetProcessById(pid);
    }
    catch (ArgumentException)
    {
      Console.WriteLine("Found lockfile but process was not running");
      DeleteLockFile();
      return null;
    }
  }

  /// <summary>
  /// Check if the lock file exists; NOT the same as checking lockfile and process!
  /// </summary>
  private bool LockFileExists()
  {
    return File.Exists(LockPath);
  }

  private LockFileContents? ReadLockFile()
  {
    if (!LockFileExists())
      return null;

    try
    {
      var txt = File.ReadAllText(LockPath);
      return LockFileContents.Deserialize(txt);
    }
    catch
    {
      Console.WriteLine("failed to read or parse the file, so assume it's invalid");
      DeleteLockFile();
      return null;
    }
  }

  /// <summary>
  /// Delete the lock file if any.
  /// </summary>
  /// <returns>true if deleted.</returns>
  private bool DeleteLockFile()
  {
    if (!LockFileExists())
      return false;

    try
    {
      File.Delete(LockPath);
      return true;
    }
    catch (Exception e)
    {
      Console.WriteLine($"Lock file exists but failed to delete it: {e.Message}");
      return false;
    }
  }

  public class LockFileHandle : IDisposable
  {
    private bool _disposed = false;

    private readonly string _lockFile;

    public LockFileHandle(string lockFile)
    {
      _lockFile = lockFile;
    }

    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    public void Dispose(bool disposing)
    {
      if (_disposed)
        return;

      if (disposing)
      {
        try
        {
          File.Delete(_lockFile);
        }
        catch
        {
          // ignored
        }
      }

      _disposed = true;
    }
  }
}

public record LockFileContents(
  int Pid
)
{
  public string Serialize()
    => JsonSerializer.Serialize(this, LockFileContentsContext.Default.LockFileContents);

  public static LockFileContents Deserialize(string str)
    => JsonSerializer.Deserialize(str, LockFileContentsContext.Default.LockFileContents);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LockFileContents))]
[JsonSerializable(typeof(int))]
public partial class LockFileContentsContext : JsonSerializerContext;