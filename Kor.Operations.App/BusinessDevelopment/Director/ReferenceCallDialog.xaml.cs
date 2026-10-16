#nullable enable
using System.Windows;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>Records a call to a reference: what happened, who was reached, and notes.</summary>
public partial class ReferenceCallDialog : Window
{
    private sealed record Option(string Value, string Label);

    public ReferenceCallDialog(BdReferenceRow reference)
    {
        InitializeComponent();
        WhoText.Text = $"{reference.Project}\n{reference.Role}: {reference.Company ?? "no company given"}"
            + (reference.ContactName is { } n ? $", {n}" : "") + (reference.Phone is { } p ? $" · {p}" : "");
        OutcomeBox.ItemsSource = new[]
        {
            new Option("Confirmed", "Confirmed: they will give a good reference"),
            new Option("LeftMessage", "Left a message"),
            new Option("NoLongerThere", "No longer works there"),
            new Option("Declined", "Declined to be a reference"),
        };
        OutcomeBox.SelectedIndex = 0;
        NameBox.Text = reference.ContactName ?? "";
    }

    public string Outcome { get; private set; } = "Confirmed";
    public string? ContactName { get; private set; }
    public string? Notes { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Outcome = OutcomeBox.SelectedValue as string ?? "Confirmed";
        ContactName = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim();
        Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        DialogResult = true;
    }
}
