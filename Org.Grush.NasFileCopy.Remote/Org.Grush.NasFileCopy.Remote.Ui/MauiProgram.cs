using Microsoft.Extensions.Logging;
using Org.Grush.NasFileCopy.Remote.Share;
using Org.Grush.NasFileCopy.Remote.Ui.DiTokens;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui;

public static class MauiProgram
{
  public static MauiApp CreateMauiApp()
  {
    bool useFileStorage = true;

    var builder = MauiApp.CreateBuilder();
    builder
      .UseMauiApp<App>()
      .ConfigureFonts(fonts =>
      {
        fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
        fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
      });

    builder.Services
      .AddNasFileCopy()
      .AddSingleton(_ => new ExeDirectory(new(AppDomain.CurrentDomain.BaseDirectory)))
    ;

    if (useFileStorage)
      builder.Services.AddSingleton<IStorageService, FileStorageService>();
    else
      throw new NotImplementedException();

#if DEBUG
    builder.Logging.AddDebug();
#endif

    return builder.Build();
  }
}