#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.Opportunities;

public partial class OpportunitiesWindow : Window
{
    private readonly OpportunitiesViewModel _vm;
    private CancellationTokenSource? _cts;

    public OpportunitiesWindow(OpportunitiesViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
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
