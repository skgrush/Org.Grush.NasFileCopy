using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Org.Grush.NasFileCopy.Remote.Ui.Components;

public partial class ConfigurationModal : ContentPage
{
  public ConfigurationModal()
  {
    InitializeComponent();
  }

  private void BackToolbarBtn_OnClicked(object? sender, EventArgs e)
  {
    Navigation.PopModalAsync().ConfigureAwait(false);
  }
}