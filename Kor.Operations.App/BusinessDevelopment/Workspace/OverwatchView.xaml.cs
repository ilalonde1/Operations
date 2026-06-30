#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.Options;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// Manager overwatch board — read-only list of every owned, active pursuit
/// with its staleness signal, hosted in <c>BdWorkspaceWindow</c>'s ContentHost
/// via the "Overwatch" nav button. Registers its VM as an AI context provider.
/// </summary>
public partial class OverwatchView : UserControl
{
    private readonly OverwatchViewModel _vm;
    private CancellationTokenSource? _cts;
    private bool _initialized;

    public OverwatchView(OverwatchViewModel vm)
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

    private async void ReassignButton_Click(object sender, RoutedEventArgs e)
    {
        var row = _vm.Selected;
        if (row is null)
        {
            return;
        }

        var dlg = new ReassignDialog(row.ProjectName, row.OwnerDisplay, _vm.KnownOwners)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var outcome = await _vm.ReassignAsync(row, dlg.TargetOwner, ResolveActor(), dlg.Reason, CancellationToken.None)
                .ConfigureAwait(true);
            if (outcome != ReassignOutcome.Reassigned)
            {
                // Not-moved cases (already moved / target duplicate) — surface the why.
                MessageBox.Show(Window.GetWindow(this), _vm.StatusMessage, "Reassign", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Reassign failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Acting manager identity for the assignment audit — same
    /// resolution the Opportunities/Bazaar views use.</summary>
    private static string ResolveActor()
    {
        if (!string.IsNullOrWhiteSpace(global::Kor.Operations.OperationsApp.SignedInUserUpn))
        {
            return global::Kor.Operations.OperationsApp.SignedInUserUpn.Trim();
        }

        var overrideUpn = AppServices.Get<UserOptions>().UserUpnOverride;
        if (!string.IsNullOrWhiteSpace(overrideUpn))
        {
            return overrideUpn.Trim();
        }

        return Environment.UserName;
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
