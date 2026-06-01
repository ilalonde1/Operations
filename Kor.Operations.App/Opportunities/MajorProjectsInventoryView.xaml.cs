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

    private void OpenOrgDossier(long canonicalOrgId)
    {
        var win = new OrgDossierWindow(_services.GetRequiredService<OrgDossierViewModel>(), canonicalOrgId)
        {
            Owner = Window.GetWindow(this),
        };
        win.Show();
    }
}
