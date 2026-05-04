#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
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
            MessageBox.Show(this, ex.Message, "Insert failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
                "Concurrent edit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
    /// Creates a CRM engagement from the selected opportunity (or opens the
    /// existing one) and shows the CRM window. Idempotent: if an engagement
    /// already exists for this opportunity we don't duplicate it.
    /// </summary>
    private async void PromoteToCrmButton_Click(object sender, RoutedEventArgs e)
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
                    Stage = CrmEngagementStage.Pursuing,
                    OwnerStaffId = _vm.Selected.Model.OwnerStaffId,
                };
                await engagementStore.InsertAsync(draft, ResolveActor(), CancellationToken.None).ConfigureAwait(true);
            }

            // Bump the opportunity to "Pursuing" too so the two pipelines stay aligned.
            try
            {
                if (_vm.Selected.Model.Status is OpportunityStatus.Identified or OpportunityStatus.Reviewing or OpportunityStatus.Qualified)
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
            MessageBox.Show(this, ex.Message, "Promote to CRM failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            MessageBox.Show(this, ex.Message, "Run Now failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RunNowButton.IsEnabled = true;
            RunNowButton.Content = btnContent;
        }
    }

    /// <summary>
    /// Promotes every High-tier Opportunity that's still in-flight (and isn't
    /// already engaged) into a CrmEngagement at stage Pursuing, provided the
    /// deadline is at least 14 days out. Confirms with the user up front so the
    /// click never silently writes a batch of CRM rows.
    /// </summary>
    private async void AutoPromoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (AutoPromoteButton is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "Create a CRM engagement (stage Pursuing) for every High-tier Opportunity " +
            "that doesn't already have one and has at least 14 days before its deadline?",
            "Auto-Promote",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        var btnContent = AutoPromoteButton.Content;
        try
        {
            AutoPromoteButton.IsEnabled = false;
            AutoPromoteButton.Content = "Promoting…";
            var result = await _vm.AutoPromoteHighTierAsync(ResolveActor(), 14, CancellationToken.None).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);

            MessageBox.Show(
                this,
                $"Considered: {result.Considered} High-tier Opportunity(ies)\n" +
                $"Already engaged (skipped): {result.Skipped}\n" +
                $"Too close to deadline: {result.InsufficientTime}\n" +
                $"Promoted: {result.PromotedOpportunityIds.Count}",
                "Auto-Promote complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Auto-Promote failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AutoPromoteButton.IsEnabled = true;
            AutoPromoteButton.Content = btnContent;
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

    private async void StatusButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null || sender is not Button btn || btn.Tag is not string tagText)
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
                "Concurrent edit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Status change failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
