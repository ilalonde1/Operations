#nullable enable
using System.Windows;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// Reason picker for the "Not for us" lifecycle action (migration 282). The
/// reason is the audit trail — the dialog will not close affirmed without one.
/// The row is never deleted; admins see it under the Removed view with
/// who/when/why and can restore it.
/// </summary>
public partial class DismissReasonDialog : Window
{
    private static readonly string[] Reasons =
    {
        "Not our discipline",
        "Wrong region",
        "Too small",
        "Structural seat is captive (alliance / in-house)",
        "Duplicate of another entry",
        "Client conflict",
        "Other (explain in note)",
    };

    public DismissReasonDialog(string targetName)
    {
        InitializeComponent();
        TargetText.Text = $"Remove “{targetName}” from the actionable pool?";
        foreach (var r in Reasons)
        {
            ReasonBox.Items.Add(r);
        }
        ReasonBox.SelectedIndex = 0;
    }

    /// <summary>Reason + optional note, composed for the audit row.</summary>
    public string Reason { get; private set; } = "";

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var reason = ReasonBox.SelectedItem as string ?? "";
        var note = NoteBox.Text?.Trim();
        if (reason.StartsWith("Other", System.StringComparison.Ordinal) && string.IsNullOrWhiteSpace(note))
        {
            MessageBox.Show(this, "\"Other\" needs a note — the reason is the audit trail.",
                "Not for us", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Reason = string.IsNullOrWhiteSpace(note) ? reason : $"{reason} — {note}";
        DialogResult = true;
    }
}
