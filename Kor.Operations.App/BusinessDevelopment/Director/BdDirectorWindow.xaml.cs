#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Options;
using Kor.Operations.App.Opportunities;
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
    private bool _ledgerBusy;
    private string _actor = "";

    public BdDirectorWindow(BdDirectorViewModel vm, IProposalStaffStore staffStore)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _staffStore = staffStore ?? throw new ArgumentNullException(nameof(staffStore));
        InitializeComponent();
        DataContext = vm;
        Loaded += Window_Loaded;
        Closed += (_, _) => { _lifetime.Cancel(); _vm.Dispose(); _lifetime.Dispose(); };
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
        if (_ledgerBusy || _reassignBusy) return;
        try { await _vm.LoadAsync(_lifetime.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { /* Window closed. */ }
    }

    private async void Reassign_Click(object sender, RoutedEventArgs e)
    {
        if (_ledgerBusy || _reassignBusy || _vm.IsLoading || sender is not FrameworkElement { DataContext: BdDirectorPursuitRow row }) return;
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

    private async void SetTarget_Click(object sender, RoutedEventArgs e) => await LedgerActionAsync(async ct =>
    {
        var people = await StaffDirectory.LoadOptionsAsync(_staffStore).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        var dialog = new TargetDialog(people) { Owner = this };
        if (dialog.ShowDialog() == true) await _vm.SetTargetAsync(dialog.Result, _actor, ct).ConfigureAwait(true);
    }).ConfigureAwait(true);

    private async void RetireTarget_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTarget is not { } row) return;
        await LedgerActionAsync(ct => _vm.RetireTargetAsync(row.Target.Id, _actor, ct)).ConfigureAwait(true);
    }

    private async void Ask_Click(object sender, RoutedEventArgs e)
        => await LedgerActionAsync(ct => ShowAskAsync(null, null, null, ct)).ConfigureAwait(true);

    private async void AskPursuit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BdDirectorPursuitRow row }) return;
        // A legacy owner spelling resolves to the same mailbox used by the roster
        // and by the recipient's dashboard.
        var personKey = _vm.People.FirstOrDefault(p => p.OwnerKeys.Contains(row.Pursuit.OwnerStaffId, StringComparer.OrdinalIgnoreCase))?.PersonKey
            ?? row.Pursuit.OwnerStaffId;
        await LedgerActionAsync(ct => ShowAskAsync(row.PursuitId, null, personKey, ct)).ConfigureAwait(true);
    }

    private async void AskAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedAccount is not { } row) return;
        await LedgerActionAsync(ct => ShowAskAsync(null, row.Account.CanonicalOrgId, null, ct)).ConfigureAwait(true);
    }

    private async Task ShowAskAsync(long? pursuitId, long? accountId, string? personKey, CancellationToken ct)
    {
        var people = await StaffDirectory.LoadOptionsAsync(_staffStore).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        var dialog = new AskDialog(people, pursuitId, accountId, personKey) { Owner = this };
        if (dialog.ShowDialog() == true) await _vm.CreateAskAsync(dialog.Result, _actor, ct).ConfigureAwait(true);
    }

    private async void WithdrawAsk_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedAsk is not { IsOpen: true } row) return;
        await LedgerActionAsync(ct => _vm.WithdrawAskAsync(row.Record.Id, _actor, ct)).ConfigureAwait(true);
    }

    private async void Decide_Click(object sender, RoutedEventArgs e)
        => await LedgerActionAsync(ct => ShowDecisionAsync(null, ct)).ConfigureAwait(true);

    private async void DecideAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedAccount is not { } row) return;
        await LedgerActionAsync(ct => ShowDecisionAsync(row.Account.CanonicalOrgId, ct)).ConfigureAwait(true);
    }

    private async Task ShowDecisionAsync(long? accountId, CancellationToken ct)
    {
        var dialog = new DecisionDialog(null, accountId) { Owner = this };
        if (dialog.ShowDialog() == true) await _vm.RecordDecisionAsync(dialog.Result, _actor, ct).ConfigureAwait(true);
    }

    private async void ScoreDecision_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDecision is not { IsPending: true } row || sender is not FrameworkElement { Tag: string outcome }) return;
        await LedgerActionAsync(async ct =>
        {
            var dialog = new NoteDialog("Score decision", $"{outcome}: what happened? (optional)") { Owner = this };
            if (dialog.ShowDialog() == true)
                await _vm.ScoreDecisionAsync(row.Record.Id, outcome, dialog.Note, _actor, ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task LedgerActionAsync(Func<CancellationToken, Task> action)
    {
        if (_ledgerBusy || _reassignBusy || _vm.IsLoading) return;
        _ledgerBusy = true;
        try { await action(_lifetime.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { /* Window closed. */ }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not save a change.");
            MessageBox.Show(this, "Could not save the change. Refresh and try again.", "BD Director", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _ledgerBusy = false; }
    }

    private void OpenDossier_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedAccount is not { } row) return;
        try { new OrgDossierWindow(AppServices.Get<OrgDossierViewModel>(), row.Account.CanonicalOrgId) { Owner = this }.Show(); }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not open dossier.");
            MessageBox.Show(this, "Could not open the account. Please try again.", "Open dossier", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
