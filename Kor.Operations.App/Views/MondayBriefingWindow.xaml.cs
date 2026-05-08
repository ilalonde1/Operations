#nullable enable
using System.Windows;

namespace Kor.Operations.App.Views;

internal partial class MondayBriefingWindow : Window
{
    private readonly MondayBriefingViewModel _vm;

    public MondayBriefingWindow(MondayBriefingViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;
        Loaded += MondayBriefingWindow_Loaded;
    }

    private void MondayBriefingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _vm.RefreshCommand.Execute(null);
    }
}
