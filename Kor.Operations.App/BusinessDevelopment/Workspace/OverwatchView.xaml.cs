#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.Options;
using Kor.Operations.Graph;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// Manager overwatch board — read-only list of every owned, active pursuit
/// with its staleness signal, plus one-click reassign. Hosted in
/// <c>BdWorkspaceWindow</c>'s ContentHost via the "Overwatch" nav button.
/// </summary>
public partial class OverwatchView : UserControl
{
    private readonly OverwatchViewModel _vm;
    private readonly IGraphFacade _graph;
    private readonly Kor.Operations.Core.Services.IProposalStaffStore _staffStore;
    private CancellationTokenSource? _cts;
    private bool _initialized;
    private bool _reassignBusy;

    public OverwatchView(OverwatchViewModel vm, IGraphFacade graph, Kor.Operations.Core.Services.IProposalStaffStore staffStore)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _staffStore = staffStore ?? throw new ArgumentNullException(nameof(staffStore));
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

    /// <summary>Double-click = open and manage the pursuit: navigate the
    /// workspace to Pursuits with this engagement pre-selected.</summary>
    private void OverwatchGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var row = _vm.Selected;
        if (row is null)
        {
            return;
        }

        if (Window.GetWindow(this) is BdWorkspaceWindow workspace)
        {
            workspace.NavigateToPursuit(row.EngagementId);
        }
    }

    private async void ReassignButton_Click(object sender, RoutedEventArgs e)
    {
        // Guard the WHOLE flow (directory load + dialog + reassign + email), not
        // just the VM reassign — otherwise a double-click opens two dialogs.
        if (_reassignBusy)
        {
            return;
        }

        var row = _vm.Selected;
        if (row is null)
        {
            return;
        }

        _reassignBusy = true;
        try
        {
            var managerUpn = ResolveActor();
            var people = await LoadDirectoryAsync().ConfigureAwait(true);

            var dlg = new ReassignDialog(row.ProjectName, row.OwnerDisplay, people)
            {
                Owner = Window.GetWindow(this),
            };
            if (dlg.ShowDialog() != true)
            {
                return;
            }

            var outcome = await _vm.ReassignAsync(row, dlg.TargetOwner, managerUpn, dlg.Reason, CancellationToken.None)
                .ConfigureAwait(true);
            if (outcome == ReassignOutcome.Reassigned)
            {
                await TrySendReassignEmailAsync(managerUpn, dlg.TargetEmail, dlg.TargetDisplay, row, dlg.Reason).ConfigureAwait(true);
            }
            else if (outcome != ReassignOutcome.Ignored)
            {
                // Not-moved cases (already moved / target duplicate) — surface the why.
                // Ignored = a dropped re-entrant click; showing anything would lie.
                MessageBox.Show(Window.GetWindow(this), _vm.StatusMessage, "Reassign", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Reassign failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _reassignBusy = false;
        }
    }

    /// <summary>
    /// The FIRM's staff list (IProposalStaffStore — the same curated roster the
    /// fee-proposal builder's "Manage Staff" maintains), not the acting user's
    /// personal transmittals-team cache: that cache only held the manager's own
    /// team (2 people), which made the reassign dropdown useless. Staff without
    /// an email are still listed (their FullName becomes the stored owner —
    /// matching the legacy first-name owners); the email notification is simply
    /// skipped for them. Failures degrade to an empty list — the picker stays
    /// editable so a manager can always type a person.
    /// </summary>
    private async Task<IReadOnlyList<PersonOption>> LoadDirectoryAsync()
    {
        try
        {
            var staff = await _staffStore.LoadAllAsync(CancellationToken.None).ConfigureAwait(true);
            return staff
                .Where(s => !string.IsNullOrWhiteSpace(s.FullName))
                .GroupBy(s => string.IsNullOrWhiteSpace(s.Email) ? s.FullName : s.Email, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(s => new PersonOption(
                    s.FullName.Trim(),
                    string.IsNullOrWhiteSpace(s.Email) ? s.FullName.Trim() : s.Email.Trim()))
                .OrderBy(p => p.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<PersonOption>();
        }
    }

    /// <summary>
    /// Notify the new owner by email (sent as the signed-in manager). The reassign
    /// has already committed; an email failure is surfaced as a warning, never an
    /// error — and is skipped entirely when the owner isn't an email address
    /// (e.g. a legacy first-name owner).
    /// </summary>
    private async Task TrySendReassignEmailAsync(
        string managerUpn, string toEmail, string toDisplay, OverwatchRowView row, string? reason)
    {
        if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@'))
        {
            return;
        }

        try
        {
            var subject = $"Pursuit assigned to you: {row.ProjectName}";
            var reasonHtml = string.IsNullOrWhiteSpace(reason)
                ? string.Empty
                : $"<p><b>Note:</b> {WebUtility.HtmlEncode(reason)}</p>";
            var buyer = string.IsNullOrWhiteSpace(row.Buyer)
                ? string.Empty
                : $" ({WebUtility.HtmlEncode(row.Buyer)})";
            var body =
                $"<p>Hi {WebUtility.HtmlEncode(toDisplay)},</p>" +
                $"<p>You've been assigned the pursuit <b>{WebUtility.HtmlEncode(row.ProjectName)}</b>{buyer} in the BD workspace.</p>" +
                reasonHtml +
                "<p>Open <b>Business Development &#8594; Pursuits</b> to work it.</p>" +
                $"<p style=\"color:#6B7280;font-size:12px\">Reassigned by {WebUtility.HtmlEncode(managerUpn)} via the KOR Operations app.</p>";

            await _graph.SendSimpleMailAsync(managerUpn, new[] { toEmail }, subject, body, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"The pursuit was reassigned, but the notification email to {toDisplay} could not be sent:\n\n{ex.Message}",
                "Reassigned (email not sent)",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>Acting manager identity for the assignment audit + email sender —
    /// same resolution the Opportunities/Bazaar views use.</summary>
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
