#nullable enable
using System;
using System.Linq;
using System.Windows;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public partial class DecisionDialog : Window
{
    private readonly long? _pursuitId;
    private readonly long? _accountId;
    public DecisionDialog(long? engagementId, long? canonicalOrgId)
    {
        InitializeComponent();
        _pursuitId = engagementId;
        _accountId = canonicalOrgId;
        KindBox.ItemsSource = new[] { "Go", "NoGo", "Drop", "Reassign", "Price", "Target", "Other" }
            .Select(k => new DirectorChoice(k, k == "NoGo" ? "No go" : k)).ToArray();
        KindBox.SelectedIndex = 0;
        ReviewPicker.SelectedDate = DateTime.Today.AddDays(90);
    }
    public BdDecision Result { get; private set; } = null!;
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (KindBox.SelectedItem is not DirectorChoice kind || string.IsNullOrWhiteSpace(DecisionBox.Text)
            || string.IsNullOrWhiteSpace(RationaleBox.Text) || string.IsNullOrWhiteSpace(ExpectedBox.Text)
            || ReviewPicker.SelectedDate is not { } review)
        {
            MessageBox.Show(this, "Choose a kind, record the decision and reasoning, say what is expected, and choose a review date.", "Record a decision");
            return;
        }
        Result = new BdDecision(0, kind.Key, _pursuitId, _accountId, DecisionBox.Text.Trim(), RationaleBox.Text.Trim(),
            ExpectedBox.Text.Trim(), DirectorDialogValues.LocalMidnight(review), "", DateTimeOffset.UtcNow, "Pending", null, null, null);
        DialogResult = true;
    }
}
