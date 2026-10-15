#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public partial class TargetDialog : Window
{
    public TargetDialog(IReadOnlyList<PersonOption> people)
    {
        InitializeComponent();
        MetricBox.ItemsSource = BdTargetMetrics.All.Select(m => new DirectorChoice(m, BdDirectorTargetRow.MetricName(m))).ToArray();
        ScopeBox.ItemsSource = new[] { "Firm", "Region", "Person" };
        MetricBox.SelectedIndex = ScopeBox.SelectedIndex = 0;
        PersonBox.ItemsSource = people;
        var today = DateTime.Today;
        var start = new DateTime(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1);
        StartPicker.SelectedDate = start;
        EndPicker.SelectedDate = start.AddMonths(3);
        ApplyScope();
    }

    public BdTarget Result { get; private set; } = null!;
    private void Scope_Changed(object sender, SelectionChangedEventArgs e) => ApplyScope();
    private void ApplyScope()
    {
        if (KeyBox is null || PersonBox is null) return;
        KeyBox.IsEnabled = (string?)ScopeBox.SelectedItem != "Firm";
        PersonBox.IsEnabled = (string?)ScopeBox.SelectedItem == "Person";
    }
    private void Person_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PersonBox.SelectedItem is PersonOption person) KeyBox.Text = person.Email;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var scope = ScopeBox.SelectedItem as string;
        if (MetricBox.SelectedItem is not DirectorChoice metric || scope is null
            || (scope != "Firm" && string.IsNullOrWhiteSpace(KeyBox.Text))
            || StartPicker.SelectedDate is not { } start || EndPicker.SelectedDate is not { } end || end <= start
            || !decimal.TryParse(ValueBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
            || value < 0 || value >= 10000000000000000m || decimal.Round(value, 2) != value)
        {
            MessageBox.Show(this, "Choose what and who the target covers, an end after its start, and a non-negative number with at most two decimal places.", "Set a target");
            return;
        }
        Result = new BdTarget(0, metric.Key, scope, scope == "Firm" ? null : KeyBox.Text.Trim(),
            DirectorDialogValues.LocalMidnight(start), DirectorDialogValues.LocalMidnight(end), value,
            DirectorDialogValues.Optional(NoteBox.Text), "", DateTimeOffset.UtcNow);
        DialogResult = true;
    }
}

public sealed record DirectorChoice(string Key, string Display);
internal static class DirectorDialogValues
{
    internal static DateTimeOffset LocalMidnight(DateTime day)
        => new DateTimeOffset(DateTime.SpecifyKind(day.Date, DateTimeKind.Local)).ToUniversalTime();
    internal static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
