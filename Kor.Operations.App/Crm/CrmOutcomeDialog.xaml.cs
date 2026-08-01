#nullable enable
using System.Windows;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;

namespace Kor.Operations.App.Crm;

/// <summary>
/// Optional one-click close-out prompt shown when a pursuit moves to Lost
/// (plan 1.5 — the deferred-register "NoBid/Withdrawn outcomes have no UI
/// affordance" item, executed). Skip is the default: nothing here is ever
/// mandatory. Saving records WonLostOutcome (Lost/NoBid/Withdrawn), the
/// winner's canonical org (Lost only, via the shared typeahead), and a
/// one-line reason.
/// </summary>
public partial class CrmOutcomeDialog : Window
{
    private long? _lostToOrgId;

    public CrmOutcomeDialog(string projectName, ICanonicalOrgStore orgStore)
    {
        InitializeComponent();
        HeaderText.Text = $"“{projectName}” is marked Lost — capture how it ended? (optional)";
        LostToSearch.Store = orgStore;
        LostToSearch.OrgSelected += (_, orgId) =>
        {
            _lostToOrgId = orgId;
            LostToPicked.Text = "Winner recorded — save to keep it.";
            LostToPicked.Visibility = Visibility.Visible;
        };
    }

    /// <summary>The chosen outcome; only meaningful when the dialog returned true.</summary>
    public WonLostOutcome Outcome { get; private set; } = WonLostOutcome.Lost;

    /// <summary>Winner's canonical org id (Lost only), when picked.</summary>
    public long? LostToCanonicalOrgId { get; private set; }

    /// <summary>Optional one-line reason.</summary>
    public string? Reason { get; private set; }

    private void OutcomeRadio_Checked(object sender, RoutedEventArgs e)
    {
        // The "who won it" typeahead only makes sense for a competitive loss.
        if (LostToLabel is null || LostToSearch is null)
        {
            return; // radios fire during InitializeComponent before siblings exist
        }

        var isLost = LostRadio.IsChecked == true;
        var vis = isLost ? Visibility.Visible : Visibility.Collapsed;
        LostToLabel.Visibility = vis;
        LostToSearch.Visibility = vis;
        LostToPicked.Visibility = isLost && _lostToOrgId.HasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Outcome = NoBidRadio.IsChecked == true ? WonLostOutcome.NoBid
            : WithdrawnRadio.IsChecked == true ? WonLostOutcome.Withdrawn
            : WonLostOutcome.Lost;
        LostToCanonicalOrgId = Outcome == WonLostOutcome.Lost ? _lostToOrgId : null;
        var reason = (ReasonBox.Text ?? string.Empty).Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        DialogResult = true;
    }
}
