using Microsoft.Extensions.Logging;
using Org.Grush.NasFileCopy.Remote.Share;
using Org.Grush.NasFileCopy.Remote.Ui.Components;
using Org.Grush.NasFileCopy.Remote.Ui.DiTokens;
using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui;

public static class MauiProgram
{
  public static readonly IReadOnlySet<DevicePlatform> ProtectivePlatforms = new HashSet<DevicePlatform>
  {
    DevicePlatform.Android,
    DevicePlatform.iOS,
    DevicePlatform.tvOS,
    DevicePlatform.watchOS,
    DevicePlatform.MacCatalyst,
  };


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

    var exeDir = new ExeDirectory(new(AppDomain.CurrentDomain.BaseDirectory));

    builder.Services
      .AddTransient<MainPage>()
      .AddTransient<ConfigurationModal>()
    ;

    builder.Services
      .AddNasFileCopy()
      .AddSingleton(exeDir)
      .AddSingleton<ConnectionService>()
      .AddSingleton<IPopUpService, PopUpService>()
    ;

    if (ProtectivePlatforms.Contains(DeviceInfo.Platform))
      builder.Services.AddSingleton(_ => new ReadWriteDirectory(new DirectoryInfo(FileSystem.CacheDirectory)));
    else
      builder.Services.AddSingleton(_ => new ReadWriteDirectory(exeDir));


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