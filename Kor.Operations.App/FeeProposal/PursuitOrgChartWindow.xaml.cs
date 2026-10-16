#nullable enable
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace Kor.Operations.App.FeeProposal;

public partial class PursuitOrgChartWindow : Window
{
    private readonly PursuitOrgChartViewModel _vm;
    private readonly CancellationTokenSource _lifetime = new();
    public PursuitOrgChartWindow(PursuitOrgChartViewModel vm)
    {
        InitializeComponent(); DataContext = _vm = vm;
        Loaded += async (_, _) => await _vm.PerformAsync(() => _vm.LoadAsync(_lifetime.Token));
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }
    private async void SaveJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Org chart JSON|*.json", FileName = "Org-Chart.json" };
        if (dialog.ShowDialog(this) == true)
            await _vm.PerformAsync(async () => { await File.WriteAllTextAsync(dialog.FileName, _vm.Json(), _lifetime.Token); _vm.Message = "Org chart specification saved."; });
    }
    private async void Render_Click(object sender, RoutedEventArgs e) => await _vm.PerformAsync(() => _vm.RenderAndAttachAsync(_lifetime.Token));
}
