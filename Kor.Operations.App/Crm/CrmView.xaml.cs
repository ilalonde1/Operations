#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.FeeProposal;
using Kor.Operations.Brochures;
using Kor.Operations.Controls;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kor.Operations.App.Crm;

/// <summary>
/// Inline UserControl version of the Pursuits (CRM) screen. Hosted in
/// <c>BdWorkspaceWindow</c>'s ContentHost via the "Pursuits" nav button.
/// Mirrors <c>CrmWindow</c>'s logic. Sub-window drill-downs (Fee Proposal
/// Builder, Brochure Builder, Client Intelligence, Engagement edit dialog)
/// still open as Windows owned by the workspace.
/// </summary>
public partial class CrmView : UserControl
{
    private readonly CrmViewModel _vm;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _cts;
    private bool _initialized;

    public CrmView(CrmViewModel vm, IServiceProvider services)
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
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync().ConfigureAwait(true);
    }

    private void BuildFeeProposalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var win = _services.GetRequiredService<FeeProposalBuilderWindow>();
        win.Owner = Window.GetWindow(this);

        var key = _vm.Selected.OpportunityKey;
        var name = string.IsNullOrWhiteSpace(_vm.Selected.ProjectName) ? key : $"{key} — {_vm.Selected.ProjectName}";

        win.Loaded += (_, _) =>
        {
            try
            {
                if (win.DataContext is FeeProposalBuilderViewModel proposalVm)
                {
                    proposalVm.StartFromOpportunity(name);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Fee-proposal prefill from CRM engagement failed; user will see an empty builder.");
                _vm.SetStatusMessage("Fee proposal opened, but automatic prefill failed.");
            }
        };

        win.Show();
    }

    private void BuildBrochureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var win = _services.GetRequiredService<BrochureBuilderWindow>();
        win.Owner = Window.GetWindow(this);

        var key = _vm.Selected.OpportunityKey;
        var name = string.IsNullOrWhiteSpace(_vm.Selected.ProjectName) ? key : $"{key} — {_vm.Selected.ProjectName}";

        win.Loaded += (_, _) =>
        {
            try
            {
                if (win.DataContext is BrochureBuilderViewModel brochureVm)
                {
                    brochureVm.StartFromOpportunity(name);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Brochure prefill from CRM engagement failed; user will see an empty builder.");
                _vm.SetStatusMessage("Brochure opened, but automatic prefill failed.");
            }
        };

        win.Show();
    }

    private void ClientIntelligenceButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var deltekId = _vm.Selected?.Opportunity?.DeltekClientId;
        if (string.IsNullOrWhiteSpace(deltekId))
        {
            MessageBox.Show(owner,
                "This engagement isn't linked to a Deltek client.",
                "CRM — Client Intelligence",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var win = _services.GetRequiredService<ClientIntelligenceWindow>();
        win.PendingClientId = deltekId;
        win.Owner = owner;
        win.Show();
    }

    private void SetStageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private async void StageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null || sender is not MenuItem mi || mi.Tag is not string tagText)
        {
            return;
        }

        if (!Enum.TryParse<CrmEngagementStage>(tagText, out var newStage))
        {
            return;
        }

        try
        {
            await _vm.ChangeStageAsync(_vm.Selected, newStage, ResolveActor(), CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "CRM — Stage Change Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditEngagementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var dlg = new CrmEngagementDialog(_vm.Selected.Engagement) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || dlg.Result is null)
        {
            return;
        }

        _ = SaveEditedEngagementAsync(dlg.Result);
    }

    private async Task SaveEditedEngagementAsync(CrmEngagement edited)
    {
        try
        {
            await _vm.SaveEngagementAsync(edited, ResolveActor(), CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "CRM — Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var name = ContactNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(owner, "Enter a display name first.", "CRM — Add Contact", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _vm.AddContactAsync(
                _vm.Selected.Id,
                name,
                NullIfBlank(ContactRoleBox.Text),
                NullIfBlank(ContactEmailBox.Text),
                NullIfBlank(ContactPhoneBox.Text),
                ContactPrimaryCheck.IsChecked == true,
                ResolveActor(),
                CancellationToken.None).ConfigureAwait(true);

            ContactNameBox.Clear();
            ContactRoleBox.Clear();
            ContactEmailBox.Clear();
            ContactPhoneBox.Clear();
            ContactPrimaryCheck.IsChecked = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "CRM — Add Contact Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RemoveContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContactsGrid.SelectedItem is not CrmContactRowView row)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        if (MessageBox.Show(owner, $"Remove contact {row.DisplayName}?", "CRM — Remove Contact",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _vm.DeleteContactAsync(row, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "CRM — Remove Contact Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LogActivityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var subject = ActivitySubjectBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(subject))
        {
            MessageBox.Show(owner, "Enter a subject for the activity.", "CRM — Log Activity", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var type = (CrmActivityType)(ActivityTypeBox.SelectedItem ?? CrmActivityType.Note);

        try
        {
            await _vm.AppendActivityAsync(
                _vm.Selected.Id,
                type,
                subject,
                NullIfBlank(ActivityBodyBox.Text),
                ResolveActor(),
                CancellationToken.None).ConfigureAwait(true);

            ActivitySubjectBox.Clear();
            ActivityBodyBox.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "CRM — Log Activity Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private string ResolveActor()
    {
        // The CrmView is hosted inside BdWorkspaceWindow; its KorHeader carries
        // the signed-in user's email. Fall back to Environment.UserName if the
        // header isn't reachable (early-loaded or no email set).
        var ownerWindow = Window.GetWindow(this);
        if (ownerWindow?.FindName("HeaderBar") is KorHeader header
            && !string.IsNullOrWhiteSpace(header.UserEmail))
        {
            return header.UserEmail!;
        }

        return Environment.UserName;
    }
}
