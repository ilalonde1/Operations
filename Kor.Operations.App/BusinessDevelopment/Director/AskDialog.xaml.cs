#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public partial class AskDialog : Window
{
    private readonly long? _pursuitId;
    private readonly long? _accountId;
    public AskDialog(IReadOnlyList<PersonOption> people, long? engagementId, long? canonicalOrgId, string? presetPersonKey)
    {
        InitializeComponent();
        _pursuitId = engagementId;
        _accountId = canonicalOrgId;
        PersonBox.ItemsSource = people;
        PersonBox.SelectedItem = people.FirstOrDefault(p => string.Equals(p.Email, presetPersonKey, StringComparison.OrdinalIgnoreCase));
        DuePicker.SelectedDate = DateTime.Today.AddDays(7);
    }
    public BdAsk Result { get; private set; } = null!;
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (PersonBox.SelectedItem is not PersonOption person || string.IsNullOrWhiteSpace(AskBox.Text)
            || !int.TryParse(MinutesBox.Text, out var minutes) || minutes is < 5 or > 180 || DuePicker.SelectedDate is not { } due)
        {
            MessageBox.Show(this, "Choose a person, enter an ask, allow 5–180 minutes and choose a due date.", "Ask");
            return;
        }
        Result = new BdAsk(0, person.Email, _pursuitId, _accountId, AskBox.Text.Trim(), DirectorDialogValues.Optional(WhyBox.Text),
            minutes, DirectorDialogValues.LocalMidnight(due), "", DateTimeOffset.UtcNow, "Open", null, null, null);
        DialogResult = true;
    }
}
