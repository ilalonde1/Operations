#nullable enable
using System.Windows;

namespace Kor.Operations.App.Views;

internal partial class CollectionsWindow : Window
{
    private readonly CollectionsViewModel _vm;

    public CollectionsWindow(CollectionsViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;
        Loaded += CollectionsWindow_Loaded;
    }

    private void CollectionsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _vm.RefreshCommand.Execute(null);
    }
}
