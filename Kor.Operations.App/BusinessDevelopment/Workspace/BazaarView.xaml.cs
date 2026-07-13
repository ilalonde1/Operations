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
/// The Opportunity Bazaar — read-only clearing-house list of un-claimed
/// opportunities, hosted in <c>BdWorkspaceWindow</c>'s ContentHost via the
/// "Bazaar" nav button. Registers its VM as an AI context provider so the
/// un-claimed pool is visible to the assistant.
/// </summary>
public partial class BazaarView : UserControl
{
    private readonly BazaarViewModel _vm;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _cts;
    private bool _initialized;
    private bool _dossierBusy;

    public BazaarView(BazaarViewModel vm, IServiceProvider services)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _services = services ?? throw new ArgumentNullException(nameof(services));
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
        // Plan 2.2c: adoption instrumentation — fire-and-forget, expendable.
        AppServices.Get<Kor.Opportunities.Data.Crm.IBdUiOpenStore>().RecordOpen("Bazaar", ResolveActor());
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

    private async void GrabButton_Click(object sender, RoutedEventArgs e)
    {
        var row = _vm.Selected;
        if (row is null)
        {
            return;
        }

        var actor = ResolveActor();
        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            $"Grab “{row.Name}” for {actor}?\n\nIt leaves this list and becomes a Drafting pursuit you own in Pursuits.",
            "Grab opportunity",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            var result = await _vm.GrabAsync(row, actor, CancellationToken.None).ConfigureAwait(true);
            if (result.Outcome == GrabOutcome.AlreadyTaken)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"“{row.Name}” was already claimed by someone else, so it has been removed from the list.",
                    "Already taken",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (result.Outcome == GrabOutcome.Grabbed && result.EngagementId > 0
                     && Window.GetWindow(this) is BdWorkspaceWindow workspace)
            {
                // Plan 1.4: a successful grab lands the user IN the pursuit —
                // same door the Overwatch double-click uses — instead of
                // leaving them staring at the Bazaar wondering where it went.
                workspace.NavigateToPursuit(result.EngagementId);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Grab failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        var row = _vm.Selected;
        if (row is null)
        {
            return;
        }

        var dialog = new DismissReasonDialog(row.Name) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _vm.DismissAsync(row, ResolveActor(), dialog.Reason, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Remove failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Plan 1.4 — intel-informed grab: double-click opens the buyer's intel
    /// dossier (reusing OrgDossierWindow, never re-rendering intel here) so the
    /// claim decision stops being blind. Unresolved buyers get an honest
    /// message, not a broken window.
    /// </summary>
    private async void BazaarGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var row = _vm.Selected;
        if (row is null || _dossierBusy)
        {
            return;
        }

        // Only a double-click that originated ON a row opens the dossier —
        // header/scrollbar/empty-space double-clicks were opening the dossier
        // for whatever row happened to be selected (review fix 2026-07-07).
        var origin = e.OriginalSource as System.Windows.DependencyObject;
        var onRow = false;
        while (origin is not null)
        {
            if (origin is DataGridRow) { onRow = true; break; }
            origin = System.Windows.Media.VisualTreeHelper.GetParent(origin);
        }
        if (!onRow)
        {
            return;
        }

        _dossierBusy = true;
        try
        {
            var orgId = await _vm.GetBuyerCanonicalOrgIdAsync(row, CancellationToken.None).ConfigureAwait(true);
            if (orgId is null)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"“{row.BuyerName}” hasn't been resolved to a canonical org yet, so there is no dossier to open.",
                    "No buyer intel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dossierVm = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<App.Opportunities.OrgDossierViewModel>(_services);
            var win = new App.Opportunities.OrgDossierWindow(dossierVm, orgId.Value)
            {
                Owner = Window.GetWindow(this),
            };
            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Buyer intel failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _dossierBusy = false;
        }
    }

    /// <summary>Current actor identity for ownership/audit — same resolution
    /// the Opportunities view uses (signed-in UPN → override → Windows user).</summary>
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
