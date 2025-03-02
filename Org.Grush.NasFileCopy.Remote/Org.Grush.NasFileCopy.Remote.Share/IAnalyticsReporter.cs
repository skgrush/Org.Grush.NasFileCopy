using Microsoft.Extensions.Logging;

namespace Org.Grush.NasFileCopy.Remote.Share;

public interface IAnalyticsReporter : ILogger
{
}

internal class AnalyticsReporter : IAnalyticsReporter
{
  private readonly TextWriter _writer = Console.Error;

  private string ScopePrefix { get; set; } = "";

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
  {
    // TODO
    _writer.WriteLine("{3}Analytics[{0},{1}]: {2}", logLevel, eventId, formatter(state, exception), ScopePrefix);
  }

  public bool IsEnabled(LogLevel logLevel)
    => true;

  public IDisposable BeginScope<TState>(TState state) where TState : notnull
  {
    _writer.WriteLine("{0}Analytics Scope Start: {1}", ScopePrefix, state);
    ScopePrefix += ">";

    return new Disposer(() =>
    {
      _writer.WriteLine("{0}Analytics Scope End: {1}", ScopePrefix, state);
      ScopePrefix = ScopePrefix[..^1];
    });
  }

  private class Disposer(Action cb) : IDisposable
  {
    public void Dispose() => cb();
  }
}