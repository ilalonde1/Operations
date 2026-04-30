#nullable enable
using System;
using System.Windows;

namespace Kor.Operations.Compensation;

public partial class CompensationWindow : Window
{
    private readonly CompensationViewModel _vm;

    public CompensationWindow(CompensationViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
        Loaded += CompensationWindow_Loaded;
    }

    private async void CompensationWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_vm.Employees.Count == 0 && !_vm.IsLoading)
            await _vm.LoadAsync();
    }

    private void ResetSelectedRow_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetSelectedRow();
    }
}
