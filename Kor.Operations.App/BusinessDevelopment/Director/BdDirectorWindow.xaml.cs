#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Options;
using Kor.Operations.Core.Services;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public partial class BdDirectorWindow : Window
{
    private readonly BdDirectorViewModel _vm;
    private readonly IProposalStaffStore _staffStore;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _initialized;
    private bool _reassignBusy;
    private string _actor = "";

    public BdDirectorWindow(BdDirectorViewModel vm, IProposalStaffStore staffStore)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _staffStore = staffStore ?? throw new ArgumentNullException(nameof(staffStore));
        InitializeComponent();
        DataContext = vm;
        Loaded += Window_Loaded;
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        // Resolve once for both adoption and reassignment, in the existing board's
        // precedence: signed-in identity, configured override, Windows user.
        _actor = new[] { global::Kor.Operations.OperationsApp.SignedInUserUpn,
                AppServices.Get<UserOptions>().UserUpnOverride, Environment.UserName }
            .First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
        try { AppServices.Get<IBdUiOpenStore>().RecordOpen("BdDirector", _actor); }
        catch (Exception ex) { Log.Warning(ex, "BD Director open instrumentation failed."); }
        await ReloadAsync().ConfigureAwait(true);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync().ConfigureAwait(true);

    private async Task ReloadAsync()
    {
        try { await _vm.LoadAsync(_lifetime.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { /* Window closed. */ }
    }

    private async void Reassign_Click(object sender, RoutedEventArgs e)
    {
        if (_reassignBusy || _vm.IsLoading || sender is not FrameworkElement { DataContext: BdDirectorPursuitRow row }) return;
        _reassignBusy = true;
        var ct = _lifetime.Token;
        try
        {
            var people = await StaffDirectory.LoadOptionsAsync(_staffStore).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            var dialog = new ReassignDialog(row.ProjectName, row.Pursuit.OwnerStaffId, people) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var outcome = await _vm.ReassignAsync(row, dialog.TargetOwner, _actor, dialog.Reason, ct).ConfigureAwait(true);
            if (outcome is not ReassignOutcome.Reassigned and not ReassignOutcome.Ignored)
                MessageBox.Show(this, _vm.StatusMessage, "Reassign", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { /* Window closed. */ }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director reassignment failed.");
            MessageBox.Show(this, "Could not reassign the pursuit. Refresh and try again.", "Reassign failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _reassignBusy = false; }
    }

    private void OpenPursuit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: long pursuitId }) return;
        try
        {
            var window = AppServices.Get<BdWorkspaceWindow>();
            window.Owner = this;
            window.NavigateToPursuit(pursuitId);
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not open pursuit {PursuitId}.", pursuitId);
            MessageBox.Show(this, "Could not open the pursuit. Please try again.", "Open pursuit",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
