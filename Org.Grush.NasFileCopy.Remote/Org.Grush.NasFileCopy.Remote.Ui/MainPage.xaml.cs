using Org.Grush.NasFileCopy.Remote.Ui.Services;

namespace Org.Grush.NasFileCopy.Remote.Ui;

public partial class MainPage : ContentPage
{
  public MainPage(
    IPopUpService popUpService,
    IModalService modalService
  )
  {
    InitializeComponent();

    popUpService.RegisterPromptHandler(this);
    modalService.RegisterNavigation(Navigation);
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