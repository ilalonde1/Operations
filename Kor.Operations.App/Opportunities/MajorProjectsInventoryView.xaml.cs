#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Inline UserControl version of the Major Projects Inventory browser. Hosted
/// inside <c>BdWorkspaceWindow</c>'s ContentHost via the "Upcoming" nav button.
/// Mirrors the logic that used to live in <c>MajorProjectsInventoryWindow</c>
/// but drops the window-level chrome (the workspace owns the chrome).
/// </summary>
public partial class MajorProjectsInventoryView : UserControl
{
    private readonly MajorProjectsInventoryViewModel _vm;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _cts;
    private bool _initialized;

    public MajorProjectsInventoryView(MajorProjectsInventoryViewModel vm, IServiceProvider services)
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
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true,
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Major Projects Inventory - Open Link Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenProponentDossier_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected?.ProponentCanonicalOrgId is { } id)
        {
            OpenOrgDossier(id);
        }
    }

    private void OpenArchitectDossier_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected?.ArchitectCanonicalOrgId is { } id)
        {
            OpenOrgDossier(id);
        }
    }

    private void ClearFunnel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MajorProjectsInventoryViewModel vm)
        {
            vm.ClearFunnelFilter();
        }
    }

    // REF # jump box: the attack sheet prints "REF <id>"; typing it here opens
    // that play's Pursuit Brief dossier — the same target as the kor://mpi
    // deep link, for when the sheet's link isn't clickable (printed / email).
    private void RefBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OpenRef();
        }
    }

    private void OpenRef_Click(object sender, RoutedEventArgs e) => OpenRef();

    private async void OpenRef()
    {
        var raw = RefBox.Text?.Trim();
        if (!long.TryParse(raw, out var id) || id <= 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Enter a numeric REF # from the attack sheet.",
                "Open dossier", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var vm = _services.GetRequiredService<BusinessDevelopment.Workspace.PursuitBriefViewModel>();
            await vm.LoadAsync(id, CancellationToken.None).ConfigureAwait(true);
            if (vm.Brief is null)
            {
                MessageBox.Show(Window.GetWindow(this), $"No active play found for REF # {id}.",
                    "Open dossier", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            new BusinessDevelopment.Workspace.PursuitBriefWindow(vm) { Owner = Window.GetWindow(this) }.Show();
            RefBox.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Open dossier", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenOrgDossier(long canonicalOrgId)
    {
        var win = new OrgDossierWindow(_services.GetRequiredService<OrgDossierViewModel>(), canonicalOrgId)
        {
            Owner = Window.GetWindow(this),
        };
        win.Show();
    }

    // ---- Pursuit lifecycle (migration 284) ----------------------------------

    private async void OwnProject_Click(object sender, RoutedEventArgs e)
    {
        var row = _vm.Selected;
        if (row is null)
        {
            return;
        }

        var actor = ResolveActor();
        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            $"Own “{row.ProjectName}” as {actor}?\n\nIt leaves the shared boards and the weekly attack sheet. Convert it to a pursuit within 14 days or it returns to the pool (your morning digest will warn you first).",
            "Own this play",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        await _vm.OwnSelectedAsync(actor, CancellationToken.None).ConfigureAwait(true);
    }

    private async void DismissProject_Click(object sender, RoutedEventArgs e)
    {
        var row = _vm.Selected;
        if (row is null)
        {
            return;
        }

        var dialog = new BusinessDevelopment.Workspace.DismissReasonDialog(row.ProjectName)
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _vm.DismissSelectedAsync(ResolveActor(), dialog.Reason, CancellationToken.None).ConfigureAwait(true);
    }

    private async void ReleaseProject_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ReleaseSelectedAsync(ResolveActor(), CancellationToken.None).ConfigureAwait(true);
    }

    private async void RestoreProject_Click(object sender, RoutedEventArgs e)
    {
        await _vm.RestoreSelectedAsync(ResolveActor(), CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Current actor identity for ownership/audit — same resolution
    /// chain the Bazaar grab uses (signed-in UPN → override → Windows user).</summary>
    private static string ResolveActor()
    {
        if (!string.IsNullOrWhiteSpace(global::Kor.Operations.OperationsApp.SignedInUserUpn))
        {
            return global::Kor.Operations.OperationsApp.SignedInUserUpn.Trim();
        }

        var overrideUpn = AppServices.Get<Options.UserOptions>().UserUpnOverride;
        if (!string.IsNullOrWhiteSpace(overrideUpn))
        {
            return overrideUpn.Trim();
        }

        return Environment.UserName;
    }
}
