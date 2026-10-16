#nullable enable
using System;
using System.Threading;
using System.Windows;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public partial class PursuitChecklistWindow : Window
{
    private readonly PursuitChecklistViewModel _vm;
    private readonly CancellationTokenSource _lifetime = new();
    public PursuitChecklistWindow(PursuitChecklistViewModel vm)
    {
        InitializeComponent(); DataContext = _vm = vm;
        Loaded += async (_, _) => await _vm.PerformAsync(() => _vm.LoadAsync(_lifetime.Token));
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }
    private async void Add_Click(object sender, RoutedEventArgs e) => await _vm.PerformAsync(() => _vm.AddAsync(_lifetime.Token));
    private async void Generic_Click(object sender, RoutedEventArgs e) => await _vm.PerformAsync(() => _vm.AddGenericAsync(_lifetime.Token));
    private async void SetStatus_Click(object sender, RoutedEventArgs e) => await _vm.PerformAsync(() => _vm.SetStatusAsync(_lifetime.Token));
    private async void Check_Click(object sender, RoutedEventArgs e) => await _vm.PerformAsync(() => _vm.CheckDraftAsync(_lifetime.Token));
    private async void Reload_Click(object sender, RoutedEventArgs e) => await _vm.PerformAsync(() => _vm.LoadAsync(_lifetime.Token));
}
