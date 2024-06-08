namespace Org.Grush.NasFileCopy.ClientSide.Maui;

public partial class App : Application
{
  public App()
  {
    InitializeComponent();

    MainPage = new AppShell();
  }
}