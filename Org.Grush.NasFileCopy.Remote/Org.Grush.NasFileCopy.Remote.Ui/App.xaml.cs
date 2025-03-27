namespace Org.Grush.NasFileCopy.Remote.Ui;

public partial class App : Application
{
  public App()
  {
    InitializeComponent();

    // TODO
#pragma warning disable CS0618 // Type or member is obsolete
    MainPage = new AppShell();
#pragma warning restore CS0618 // Type or member is obsolete
  }
}