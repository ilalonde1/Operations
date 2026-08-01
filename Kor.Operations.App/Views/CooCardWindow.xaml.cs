#nullable enable
using System.Windows;

namespace Kor.Operations.App.Views;

internal partial class CooCardWindow : Window
{
    private readonly CooCardViewModel _vm;

    public CooCardWindow(CooCardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += CooCardWindow_Loaded;
    }

    private void CooCardWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Initial load — the refresh command is async-void on the inside so it
        // returns immediately and renders progress through the BusyVisibility
        // binding. Matches the MondayBriefingWindow startup pattern.
        _vm.RefreshCommand.Execute(null);
    }
}
