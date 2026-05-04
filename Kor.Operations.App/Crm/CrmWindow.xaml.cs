#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.FeeProposal;
using Kor.Operations.Brochures;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.Crm;

public partial class CrmWindow : Window
{
    private readonly CrmViewModel _vm;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _cts;

    public CrmWindow(CrmViewModel vm, IServiceProvider services)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeComponent();
        DataContext = _vm;

        // Per the firm-wide AI rule: every feature window registers its VM as a
        // context provider so the Ops chat can answer questions about the data.
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

    /// <summary>
    /// Hands off to the Fee Proposal Builder pre-named after this engagement.
    /// The proposal is brand-new (we don't pre-populate blocks); the user
    /// builds out the cover, scope, fee table, etc. inside the builder.
    /// </summary>
    private void BuildFeeProposalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var win = _services.GetRequiredService<FeeProposalBuilderWindow>();
        win.Owner = this;

        // Compose a deterministic name so OpenProposalDialog later can find this
        // proposal. Falls back to OpportunityKey alone if name is missing.
        var key = _vm.Selected.OpportunityKey;
        var name = string.IsNullOrWhiteSpace(_vm.Selected.ProjectName) ? key : $"{key} — {_vm.Selected.ProjectName}";

        // The VM is wired by FeeProposalBuilderWindow at ctor time. We pull it
        // back via DataContext after the window has initialized so we can pre-fill.
        win.Loaded += (_, _) =>
        {
            try
            {
                if (win.DataContext is FeeProposalBuilderViewModel proposalVm)
                {
                    proposalVm.StartFromOpportunity(name);
                }
            }
            catch
            {
                // Best-effort pre-fill; if it fails, the user just sees "Untitled Proposal".
            }
        };

        win.Show();
    }

    /// <summary>
    /// Hands off to the Sales Brochure Builder pre-named after this engagement.
    /// Mirrors <see cref="BuildFeeProposalButton_Click"/> — same engagement-key
    /// + project-name composition, same best-effort prefill via the window's
    /// DataContext after Loaded.
    /// </summary>
    private void BuildBrochureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var win = _services.GetRequiredService<BrochureBuilderWindow>();
        win.Owner = this;

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
            catch
            {
                // Best-effort pre-fill; if it fails, the user lands on the empty builder.
            }
        };

        win.Show();
    }

    private async void StageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null || sender is not Button btn || btn.Tag is not string tagText)
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
            MessageBox.Show(this, ex.Message, "Stage change failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditEngagementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var dlg = new CrmEngagementDialog(_vm.Selected.Engagement) { Owner = this };
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
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var name = ContactNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Enter a display name first.", "Add contact", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(this, ex.Message, "Add contact failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RemoveContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContactsGrid.SelectedItem is not CrmContactRowView row)
        {
            return;
        }

        if (MessageBox.Show(this, $"Remove contact {row.DisplayName}?", "Remove contact",
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
            MessageBox.Show(this, ex.Message, "Remove contact failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LogActivityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            return;
        }

        var subject = ActivitySubjectBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(subject))
        {
            MessageBox.Show(this, "Enter a subject for the activity.", "Log activity", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(this, ex.Message, "Log activity failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private string ResolveActor()
    {
        var headerEmail = HeaderBar?.UserEmail;
        return !string.IsNullOrWhiteSpace(headerEmail)
            ? headerEmail
            : Environment.UserName;
    }
}
