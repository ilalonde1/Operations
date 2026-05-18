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
            await _vm.EnsureEngagementAsync(ResolveActor(), CancellationToken.None).ConfigureAwait(true);

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
    /// Opens a source picker for enabled automated opportunity sources. The
    /// selected source is queued through the same IngestionTrigger path used by
    /// the previous CanadaBuys-only button.
    /// </summary>
    private async void RunNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (RunNowButton is null)
        {
            return;
        }

        try
        {
            RunNowButton.IsEnabled = false;
            var sources = await _vm.ListRunnableSourcesAsync(CancellationToken.None).ConfigureAwait(true);

            RunNowMenu.Items.Clear();
            if (sources.Count == 0)
            {
                RunNowMenu.Items.Add(new MenuItem
                {
                    Header = "(no enabled sources)",
                    IsEnabled = false,
                });
            }
            else
            {
                foreach (var s in sources)
                {
                    var item = new MenuItem
                    {
                        // Show source name + type so the operator knows what
                        // they're triggering, e.g. "CanadaBuys (GenericCsv)".
                        Header = $"{s.Name} ({s.SourceType})",
                        Tag = s.Name,
                        ToolTip = string.IsNullOrWhiteSpace(s.BaseUrl) ? null : s.BaseUrl,
                    };
                    item.Click += RunSourceMenuItem_Click;
                    RunNowMenu.Items.Add(item);
                }
            }

            RunNowMenu.PlacementTarget = RunNowButton;
            RunNowMenu.IsOpen = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opportunities — Run Now Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RunNowButton.IsEnabled = true;
        }
    }

    /// <summary>Click handler attached to each dynamically-built MenuItem in
    /// the Run-Source dropdown. Reads the source name from Tag, enqueues an
    /// ingestion trigger via the VM, and refreshes the runs panel after a poll
    /// cycle so the new run shows up.</summary>
    private async void RunSourceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string sourceName || string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        var btnContent = RunNowButton?.Content;
        try
        {
            if (RunNowButton is not null)
            {
                RunNowButton.IsEnabled = false;
                RunNowButton.Content = $"Queueing {sourceName}";
            }

            await _vm.RequestRunAsync(sourceName, ResolveActor(), CancellationToken.None).ConfigureAwait(true);

            // Worker picks up the trigger within one poll cycle (~30s).
            // Re-poll the IngestionRuns panel after 35s so the new run lands.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(35)).ConfigureAwait(false);
                var refreshTask = await Dispatcher.InvokeAsync(() => _vm.RefreshIngestionRunsAsync(CancellationToken.None));
                await refreshTask.ConfigureAwait(false);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, $"Opportunities — Run {sourceName} Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (RunNowButton is not null)
            {
                RunNowButton.IsEnabled = true;
                if (btnContent is not null)
                {
                    RunNowButton.Content = btnContent;
                }
            }
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
