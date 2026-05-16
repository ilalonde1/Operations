#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Kor.Operations.App.Crm;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.Opportunities;

public partial class OpportunitiesWindow : Window
{
    private readonly OpportunitiesViewModel _vm;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _cts;

    public OpportunitiesWindow(OpportunitiesViewModel vm, IServiceProvider services)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeComponent();
        DataContext = _vm;

        // Per the firm-wide AI rule (memory: feedback_ai_context_provider.md):
        // every feature window registers its VM as a context provider so the
        // Ops chat can answer questions about the visible data. AppAiContextBuilder
        // is internal so we use the service locator (same trick FileSync uses) to
        // avoid an accessibility mismatch on this public ctor.
        var aiContextBuilder = AppServices.Get<AppAiContextBuilder>();
        aiContextBuilder.Register(_vm);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync().ConfigureAwait(true);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);

        // Unregister so a closed window doesn't keep stale data in the AI context.
        AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
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
            // window closing
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync().ConfigureAwait(true);
    }

    private async void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpportunityEntryDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null)
        {
            return;
        }

        try
        {
            await _vm.InsertAsync(dlg.Result, ResolveActor(), CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opportunities — Insert Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedAsync().ConfigureAwait(true);
    }

    private async void OpportunitiesGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        await EditSelectedAsync().ConfigureAwait(true);
    }

    private async Task EditSelectedAsync()
    {
        var selected = _vm.Selected;
        if (selected is null)
        {
            return;
        }

        var dlg = new OpportunityEntryDialog(selected.Model) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null)
        {
            return;
        }

        try
        {
            await _vm.UpdateAsync(dlg.Result, ResolveActor(), CancellationToken.None).ConfigureAwait(true);
        }
        catch (OpportunityConcurrencyException)
        {
            MessageBox.Show(
                this,
                "This opportunity was modified by another user since you opened it. Click Refresh and try again.",
                "Opportunities — Concurrent Edit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opportunities — Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScoringButton_Click(object sender, RoutedEventArgs e)
    {
        var win = _services.GetRequiredService<ScoringProfileWindow>();
        win.Owner = this;
        // When the editor saves or recalcs, refresh our grid so new scores show up.
        win.ProfilePersisted += async (_, _) =>
        {
            await ReloadAsync().ConfigureAwait(true);
        };
        win.Show();
    }

    /// <summary>
    /// Creates a Pursuit engagement from the selected opportunity (or opens the
    /// existing one) and shows the CRM window. Idempotent: if an engagement
    /// already exists for this opportunity we don't duplicate it.
    /// </summary>
    private async void PromoteToPursuitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        try
        {
            var engagementStore = _services.GetRequiredService<ICrmEngagementStore>();
            var existing = await engagementStore.GetByOpportunityAsync(_vm.Selected.Id, CancellationToken.None).ConfigureAwait(true);
            if (existing is null)
            {
                var draft = new CrmEngagement
                {
                    OpportunityId = _vm.Selected.Id,
                    Stage = CrmEngagementStage.Drafting,
                    OwnerStaffId = _vm.Selected.Model.OwnerStaffId,
                };
                await engagementStore.InsertAsync(draft, ResolveActor(), CancellationToken.None).ConfigureAwait(true);
            }

            // Bump the opportunity to "Pursuing" too so the two pipelines stay aligned.
            try
            {
                if (_vm.Selected.Model.Status is OpportunityStatus.New)
                {
                    await _vm.ChangeStatusAsync(_vm.Selected, OpportunityStatus.Pursuing, ResolveActor(), CancellationToken.None).ConfigureAwait(true);
                }
            }
            catch (OpportunityConcurrencyException)
            {
                // Status change is best-effort; the engagement already exists.
            }

            var win = _services.GetRequiredService<App.Crm.CrmWindow>();
            win.Owner = this;
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opportunities — Promote To Pursuit Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenInPursuitsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedEngagement is null)
        {
            return;
        }

        var win = _services.GetRequiredService<App.Crm.CrmWindow>();
        win.Owner = this;
        win.Show();
    }

    private void OpenRfpButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _vm.SelectedSourceUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opportunities — Open RFP Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LogActivityButton_Click(object sender, RoutedEventArgs e)
    {
        await LogActivityFromInputAsync().ConfigureAwait(true);
    }

    private async void ActivitySubjectBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            await LogActivityFromInputAsync().ConfigureAwait(true);
        }
    }

    private async Task LogActivityFromInputAsync()
    {
        var subject = ActivitySubjectBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(subject))
        {
            return;
        }

        try
        {
            await _vm.LogActivityAsync(subject, ResolveActor(), CancellationToken.None).ConfigureAwait(true);
            ActivitySubjectBox.Clear();
            ActivitySubjectBox.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opportunities — Log Activity Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddContactButton_Click(object sender, RoutedEventArgs e)
    {
        var name = ContactNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, "Enter a contact name first.", "Opportunities — Add Contact", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _vm.AddContactAsync(
                name,
                ContactEmailBox.Text?.Trim(),
                phone: null,
                ResolveActor(),
                CancellationToken.None).ConfigureAwait(true);

            ContactNameBox.Clear();
            ContactEmailBox.Clear();
            ContactNameBox.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opportunities — Add Contact Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClientIntelligenceButton_Click(object sender, RoutedEventArgs e)
    {
        var deltekId = _vm.Selected?.Model.DeltekClientId;
        if (string.IsNullOrWhiteSpace(deltekId))
        {
            MessageBox.Show(this,
                "Select an opportunity that's linked to a Deltek client (the 'Repeat' badge column shows which).",
                "Opportunities — Client Intelligence",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var win = _services.GetRequiredService<ClientIntelligenceWindow>();
        win.PendingClientId = deltekId;
        win.Owner = this;
        win.Show();
    }

    /// <summary>
    /// Enqueues an ingestion-trigger row for the CanadaBuys source. The Worker
    /// drains the table within ~30s; we don't block the UI thread waiting —
    /// the user can hit Refresh to see the run land in the right-hand panel.
    /// </summary>
    private async void RunNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (RunNowButton is null)
        {
            return;
        }

        var btnContent = RunNowButton.Content;
        try
        {
            RunNowButton.IsEnabled = false;
            RunNowButton.Content = "Queueing…";
            await _vm.RequestRunAsync("CanadaBuys", ResolveActor(), CancellationToken.None).ConfigureAwait(true);

            // Refresh shortly after so the new IngestionRun row appears once the
            // poller picks it up. 35s gives the Worker one full poll cycle plus
            // a CSV pull margin.
            _ = ScheduleRefreshAsync(TimeSpan.FromSeconds(35));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opportunities — Run Now Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RunNowButton.IsEnabled = true;
            RunNowButton.Content = btnContent;
        }
    }

    private async Task ScheduleRefreshAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Best-effort post-trigger refresh; don't crash the window if the
            // user closed it during the wait or the DB hiccupped.
        }
    }

    private void MoveToButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private async void StatusMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null || sender is not MenuItem mi || mi.Tag is not string tagText)
        {
            return;
        }

        if (!Enum.TryParse<OpportunityStatus>(tagText, out var newStatus))
        {
            return;
        }

        try
        {
            await _vm.ChangeStatusAsync(_vm.Selected, newStatus, ResolveActor(), CancellationToken.None).ConfigureAwait(true);
        }
        catch (OpportunityConcurrencyException)
        {
            MessageBox.Show(
                this,
                "Status change blocked: the row was modified elsewhere. Click Refresh and try again.",
                "Opportunities — Concurrent Edit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opportunities — Status Change Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Display name used for CreatedBy / UpdatedBy. Falls back to the
    /// Windows account when the header hasn't resolved a UPN yet.</summary>
    private string ResolveActor()
    {
        var headerEmail = HeaderBar?.UserEmail;
        return !string.IsNullOrWhiteSpace(headerEmail)
            ? headerEmail
            : Environment.UserName;
    }
}
