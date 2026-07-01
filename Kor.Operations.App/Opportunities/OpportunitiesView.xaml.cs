#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.Crm;
using Kor.Operations.App.Options;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Opportunities;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.Opportunities;

public partial class OpportunitiesView : UserControl
{
    private readonly OpportunitiesViewModel _vm;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _cts;
    private bool _initialized;

    public OpportunitiesView(OpportunitiesViewModel vm, IServiceProvider services)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeComponent();
        DataContext = _vm;
    }

    private async void OpportunitiesView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        AppServices.Get<AppAiContextBuilder>().Register(_vm);
        await ReloadAsync().ConfigureAwait(true);
    }

    private void OpportunitiesView_Unloaded(object sender, RoutedEventArgs e)
    {
        CancelAndDisposeCts();
        if (_initialized)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
        }

        _initialized = false;
    }

    private async Task ReloadAsync()
    {
        var cts = ReplaceCts();
        try
        {
            await _vm.LoadAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // window closing
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync().ConfigureAwait(true);
    }

    private async void GenerateBriefButton_Click(object sender, RoutedEventArgs e)
        => await GenerateOpportunityBriefAsync(asPdf: true).ConfigureAwait(true);

    private async void GenerateBriefAsWord_Click(object sender, RoutedEventArgs e)
        => await GenerateOpportunityBriefAsync(asPdf: false).ConfigureAwait(true);

    // Re-entrancy guard: a double-click ran two concurrent generations +
    // Deltek pulls (audit 2026-07-01 4.5).
    private bool _briefGenerating;

    private async Task GenerateOpportunityBriefAsync(bool asPdf)
    {
        if (_briefGenerating)
        {
            return;
        }

        var row = _vm.Selected;
        if (row is null)
        {
            MessageBox.Show("Select an opportunity first.", "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _briefGenerating = true;
        try
        {
            await GenerateOpportunityBriefCoreAsync(row, asPdf).ConfigureAwait(true);
        }
        finally
        {
            _briefGenerating = false;
        }
    }

    private async Task GenerateOpportunityBriefCoreAsync(OpportunityRowView row, bool asPdf)
    {

        var briefStore = AppServices.Get<Kor.Opportunities.Data.Briefs.IBriefDataStore>();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var ext = asPdf ? "pdf" : "docx";
        var path = System.IO.Path.Combine(
            desktop,
            $"KOR-Pursuit-Brief-{row.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}");

        var previousStatus = _vm.StatusMessage;
        _vm.StatusMessage = "Generating pursuit brief…";
        try
        {
            var data = await briefStore.GetOpportunityBriefAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
            if (data is null)
            {
                _vm.StatusMessage = previousStatus;
                MessageBox.Show("Opportunity not found.", "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (asPdf)
            {
                var pdf = AppServices.Get<Kor.Operations.App.BusinessDevelopment.Briefs.IBriefPdfGenerator>();
                await Task.Run(() => pdf.WriteOpportunityBrief(data, path)).ConfigureAwait(true);
            }
            else
            {
                var docx = AppServices.Get<Kor.Operations.App.BusinessDevelopment.Briefs.IBriefGenerator>();
                await Task.Run(() => docx.WriteOpportunityBrief(data, path)).ConfigureAwait(true);
            }

            _vm.StatusMessage = "Brief saved: " + path;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = "Brief failed: " + ex.Message;
            MessageBox.Show("Brief generation failed: " + ex.Message, "Generate Brief", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpportunityEntryDialog { Owner = OwnerWindow() };
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
            MessageBox.Show(OwnerWindow(), ex.Message, "Opportunities — Insert Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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

        var dlg = new OpportunityEntryDialog(selected.Model) { Owner = OwnerWindow() };
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
                OwnerWindow(),
                "This opportunity was modified by another user since you opened it. Click Refresh and try again.",
                "Opportunities — Concurrent Edit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Opportunities — Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ScoringButton_Click(object sender, RoutedEventArgs e)
    {
        var win = _services.GetRequiredService<ScoringProfileWindow>();
        win.Owner = OwnerWindow();
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
            win.Owner = OwnerWindow();
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Opportunities — Promote To Pursuit Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenInPursuitsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedEngagement is null)
        {
            return;
        }

        var win = _services.GetRequiredService<App.Crm.CrmWindow>();
        win.Owner = OwnerWindow();
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
            MessageBox.Show(OwnerWindow(), ex.Message, "Opportunities — Open RFP Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(OwnerWindow(), ex.Message, "Opportunities — Log Activity Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddContactButton_Click(object sender, RoutedEventArgs e)
    {
        var name = ContactNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(OwnerWindow(), "Enter a contact name first.", "Opportunities — Add Contact", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(OwnerWindow(), ex.Message, "Opportunities — Add Contact Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClientIntelligenceButton_Click(object sender, RoutedEventArgs e)
    {
        var deltekId = _vm.Selected?.Model.DeltekClientId;
        var win = _services.GetRequiredService<ClientIntelligenceWindow>();
        // If an opportunity is selected with a Deltek link, prefill; otherwise the window
        // opens with the "Load by ClientID" input visible so the user can type a Clendor ID
        // directly (e.g. CL00554 City of Vancouver). Needed because most ingested opportunities
        // aren't yet linked to a Deltek client.
        if (!string.IsNullOrWhiteSpace(deltekId)) win.PendingClientId = deltekId;
        win.Owner = OwnerWindow();
        win.Show();
    }

    private void IngestionRunsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var win = _services.GetRequiredService<IngestionRunsWindow>();
            win.Owner = OwnerWindow();
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Opportunities — Open Runs Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            MessageBox.Show(OwnerWindow(), ex.Message, "Opportunities — Run Now Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(35)).ConfigureAwait(false);
                    if (!_initialized)
                    {
                        return;
                    }

                    var refreshTask = await Dispatcher.InvokeAsync(() =>
                    {
                        return _initialized
                            ? _vm.RefreshIngestionRunsAsync(CancellationToken.None)
                            : Task.CompletedTask;
                    });
                    await refreshTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (_initialized)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            _vm.StatusMessage = $"Ingestion-run refresh failed: {ex.GetType().Name}: {ex.Message}";
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, $"Opportunities — Run {sourceName} Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
                OwnerWindow(),
                "Status change blocked: the row was modified elsewhere. Click Refresh and try again.",
                "Opportunities — Concurrent Edit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow(), ex.Message, "Opportunities — Status Change Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Display name used for CreatedBy / UpdatedBy. Falls back to the Windows account.</summary>
    private string ResolveActor()
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

    private CancellationTokenSource ReplaceCts()
    {
        var old = _cts;
        _cts = new CancellationTokenSource();
        old?.Cancel();
        old?.Dispose();
        return _cts;
    }

    private void CancelAndDisposeCts()
    {
        var old = _cts;
        _cts = null;
        old?.Cancel();
        old?.Dispose();
    }

    private Window? OwnerWindow()
    {
        return Window.GetWindow(this) ?? Application.Current?.MainWindow;
    }
}
