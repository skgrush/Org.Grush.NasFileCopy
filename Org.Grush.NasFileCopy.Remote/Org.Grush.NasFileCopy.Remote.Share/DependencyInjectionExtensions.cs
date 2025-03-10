using Microsoft.Extensions.DependencyInjection;

namespace Org.Grush.NasFileCopy.Remote.Share;

public static class DependencyInjectionExtensions
{
  public static IServiceCollection AddNasFileCopy(this IServiceCollection serviceCollection)
    => serviceCollection
      .AddSingleton<ITrueNasClient, TrueNasClient>()
      .AddSingleton<TrueNasSshClient>()
      .AddSingleton<TrueNasHttpClient>()
      .AddSingleton<IAnalyticsReporter, AnalyticsReporter>()
    ;
}