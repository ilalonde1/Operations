#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.Crm;

public partial class CrmEngagementDialog : Window
{
    private readonly CrmEngagement _original;

    /// <summary>
    /// Fix F5: the owner is picked from the firm roster (same PersonOption
    /// list as the Overwatch reassign picker) instead of a free-text box that
    /// could mint new identity formats or silently BLANK the owner — a blanked
    /// OwnerStaffId made the pursuit vanish from the Overwatch board forever.
    /// The combo stays editable (legacy first-name owners still exist; D2 owns
    /// true canonicalization), but an empty box now means "keep current owner".
    /// </summary>
    public CrmEngagementDialog(CrmEngagement engagement, IReadOnlyList<PersonOption>? people = null)
    {
        _original = engagement ?? throw new ArgumentNullException(nameof(engagement));
        InitializeComponent();

        StageBox.ItemsSource = Enum.GetValues<CrmEngagementStage>();
        StageBox.SelectedItem = engagement.Stage;
        OwnerCombo.ItemsSource = people ?? Array.Empty<PersonOption>();
        OwnerCombo.Text = engagement.OwnerStaffId ?? string.Empty;
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
            OwnerStaffId = ResolveOwner(),
            ProposedFee = fee,
            ProposedHours = hours,
            TargetMargin = margin,
            Notes = NullIfBlank(NotesBox.Text),
            OutcomeNotes = NullIfBlank(OutcomeBox.Text),
        };

        DialogResult = true;
        Close();
    }

    /// <summary>Roster pick wins; typed text is accepted as-is; an EMPTY box
    /// keeps the current owner — blanking is not offered (fix F5).</summary>
    private string? ResolveOwner()
    {
        if (OwnerCombo.SelectedItem is PersonOption picked)
        {
            return picked.Email;
        }

        var typed = (OwnerCombo.Text ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(typed) ? _original.OwnerStaffId : typed;
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
