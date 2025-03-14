using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui;

public partial class MainPage : ContentPage
{
  public MainPage(IPopUpService popUpService)
  {
    InitializeComponent();

    popUpService.RegisterPromptHandler(this);
  }

  private void OnCounterClicked(object sender, EventArgs e)
  {

    // if (count == 1)
    //   CounterBtn.Text = $"Clicked {count} time";
    // else
    //   CounterBtn.Text = $"Clicked {count} times";
    //
    // SemanticScreenReader.Announce(CounterBtn.Text);
  }
}