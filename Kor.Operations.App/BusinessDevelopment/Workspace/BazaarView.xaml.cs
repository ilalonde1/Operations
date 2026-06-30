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
    private CancellationTokenSource? _cts;
    private bool _initialized;

    public BazaarView(BazaarViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
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
            $"Grab “{row.Name}” for {actor}?\n\nIt leaves the Bazaar and becomes a Drafting pursuit you own in Pursuits.",
            "Grab opportunity",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            var outcome = await _vm.GrabAsync(row, actor, CancellationToken.None).ConfigureAwait(true);
            if (outcome == GrabOutcome.AlreadyTaken)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"“{row.Name}” was already claimed by someone else, so it has been removed from the Bazaar.",
                    "Already taken",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Grab failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
