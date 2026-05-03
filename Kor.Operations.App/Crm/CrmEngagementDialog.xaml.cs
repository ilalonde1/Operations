#nullable enable
using System;
using System.Globalization;
using System.Windows;
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.Crm;

public partial class CrmEngagementDialog : Window
{
    private readonly CrmEngagement _original;

    public CrmEngagementDialog(CrmEngagement engagement)
    {
        _original = engagement ?? throw new ArgumentNullException(nameof(engagement));
        InitializeComponent();

        StageBox.ItemsSource = Enum.GetValues<CrmEngagementStage>();
        StageBox.SelectedItem = engagement.Stage;
        OwnerBox.Text = engagement.OwnerStaffId ?? string.Empty;
        FeeBox.Text = engagement.ProposedFee?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        HoursBox.Text = engagement.ProposedHours?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        MarginBox.Text = engagement.TargetMargin?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        NotesBox.Text = engagement.Notes ?? string.Empty;
        OutcomeBox.Text = engagement.OutcomeNotes ?? string.Empty;
    }

    /// <summary>Result; null if the user cancelled.</summary>
    public CrmEngagement? Result { get; private set; }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var stage = StageBox.SelectedItem is CrmEngagementStage s ? s : _original.Stage;
        var fee = ParseDecimal(FeeBox.Text);
        var hours = ParseDecimal(HoursBox.Text);
        var margin = ParseDecimal(MarginBox.Text);

        Result = _original with
        {
            Stage = stage,
            OwnerStaffId = NullIfBlank(OwnerBox.Text),
            ProposedFee = fee,
            ProposedHours = hours,
            TargetMargin = margin,
            Notes = NullIfBlank(NotesBox.Text),
            OutcomeNotes = NullIfBlank(OutcomeBox.Text),
        };

        DialogResult = true;
        Close();
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var stripped = raw.Trim().Replace(",", string.Empty).Replace("$", string.Empty).Replace("%", string.Empty);
        return decimal.TryParse(stripped, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
