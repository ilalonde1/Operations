#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Kor.Opportunities.Core.Deltek;

namespace Kor.Operations.App.Crm;

/// <summary>
/// Plan 2.5: link a Won pursuit to the Deltek project it became. Candidates
/// come from the buyer's Clendor client (live ODBC read via the cached
/// accessor); the typed-WBS1 fallback means ANY win can be linked — including
/// buyers not yet resolved to a Deltek client. Suggest/confirm, never
/// auto-write (write-gate doctrine).
/// </summary>
public partial class CrmWonProjectDialog : Window
{
    public sealed record ProjectOption(string Wbs1, string Display);

    public CrmWonProjectDialog(string projectName, IReadOnlyList<KorWonProjectRow> candidates)
    {
        InitializeComponent();
        HeaderText.Text = candidates.Count > 0
            ? $"Which Deltek project did “{projectName}” become?"
            : $"“{projectName}” — the buyer has no Deltek projects on file; type the WBS1 below.";
        ProjectList.ItemsSource = candidates
            .Select(c => new ProjectOption(
                c.Wbs1,
                $"{c.Wbs1} — {c.Name}{(c.StartDate.HasValue ? $" (opened {c.StartDate.Value:yyyy-MM})" : "")}"))
            .ToList();
    }

    /// <summary>The chosen WBS1; only meaningful when the dialog returned true.</summary>
    public string SelectedWbs1 { get; private set; } = string.Empty;

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        // Typed text wins (the any-win fallback); otherwise the list pick.
        var typed = (ManualWbs1Box.Text ?? string.Empty).Trim();
        var wbs1 = typed.Length > 0
            ? typed
            : (ProjectList.SelectedItem as ProjectOption)?.Wbs1 ?? string.Empty;

        if (string.IsNullOrWhiteSpace(wbs1))
        {
            MessageBox.Show(this, "Pick a project from the list, or type its WBS1.", "Link Deltek project",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (wbs1.Length > 50)
        {
            MessageBox.Show(this, "A WBS1 is at most 50 characters.", "Link Deltek project",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedWbs1 = wbs1;
        DialogResult = true;
    }
}
