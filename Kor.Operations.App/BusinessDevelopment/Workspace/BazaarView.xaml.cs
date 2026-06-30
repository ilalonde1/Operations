#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Services;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// The Opportunity Bazaar — read-only clearing-house list of un-claimed
/// opportunities, hosted in <c>BdWorkspaceWindow</c>'s ContentHost via the
/// "Bazaar" nav button. Registers its VM as an AI context provider so the
/// un-claimed pool is visible to the assistant.
/// </summary>
public partial class BazaarView : UserControl
{
    private readonly BazaarViewModel _vm;
    private CancellationTokenSource? _cts;
    private bool _initialized;

    public BazaarView(BazaarViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = _vm;
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        AppServices.Get<AppAiContextBuilder>().Register(_vm);
        await ReloadAsync().ConfigureAwait(true);
    }

    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        if (_initialized)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
            _initialized = false;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync().ConfigureAwait(true);
    }

    private async Task ReloadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        try
        {
            await _vm.LoadAsync(_cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // view unloading
        }
    }
}
