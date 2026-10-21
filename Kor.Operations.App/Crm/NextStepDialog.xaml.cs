#nullable enable
using System;
using System.Windows;

namespace Kor.Operations.App.Crm;

/// <summary>The lead sets their own next step and its date on a pursuit; prefilled with the current one.</summary>
public partial class NextStepDialog : Window
{
    public NextStepDialog(string pursuit, string? note, DateTimeOffset? dueUtc)
    {
        InitializeComponent();
        PursuitText.Text = pursuit;
        NoteBox.Text = note ?? "";
        DuePicker.SelectedDate = dueUtc is { } d && d.ToLocalTime().Date >= DateTime.Today ? d.ToLocalTime().Date : DateTime.Today.AddDays(7);
    }

    public string Note { get; private set; } = "";
    public DateTime DueDay { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NoteBox.Text)) { ErrorText.Text = "Enter the next step."; return; }
        if (DuePicker.SelectedDate is not { } due) { ErrorText.Text = "Choose the date it is due."; return; }
        if (due.Date < DateTime.Today) { ErrorText.Text = "Choose today or a later date."; return; }
        Note = NoteBox.Text.Trim();
        DueDay = due.Date;
        DialogResult = true;
    }
}
