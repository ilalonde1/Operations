#nullable enable
using System.Windows;
using Kor.Operations.Services;

namespace Kor.Operations.App.Opportunities;

public partial class CompetitorProfileWindow : Window
{
    private readonly CompetitorProfileViewModel _vm;
    private readonly string _vendorName;

    public CompetitorProfileWindow(CompetitorProfileViewModel vm, string vendorName)
    {
        InitializeComponent();
        _vm = vm;
        _vendorName = vendorName;
        DataContext = vm;
    }

      private async void Window_Loaded(object sender, RoutedEventArgs e)
      {
          await HeaderLoader.ApplyAsync(HeaderBar);
          await _vm.LoadAsync(_vendorName).ConfigureAwait(true);
      }

      private void OnHyperlinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
      {
          try
          {
              System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
              {
                  FileName = e.Uri.ToString(),
                  UseShellExecute = true,
              });
              e.Handled = true;
          }
          catch
          {
          }
      }
  }
