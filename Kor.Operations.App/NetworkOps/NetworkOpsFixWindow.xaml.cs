#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.App.NetworkOps;

/// <summary>Pick one fix for one finding. Returns the choice (and its input); the device window runs it.</summary>
public partial class NetworkOpsFixWindow : Window
{
    private readonly bool _someoneActive;

    public NetworkOpsFixWindow(string findingTitle, string deviceName, string presence, bool someoneActive, IReadOnlyList<FixOption> fixes)
    {
        InitializeComponent();
        _someoneActive = someoneActive;
        Title = $"Fix — {deviceName}";
        FindingTitle.Text = findingTitle;
        PresenceText.Text = string.IsNullOrEmpty(presence) ? $"{deviceName}: who is on it is not known yet." : $"{deviceName} at the last check: {presence}";
        FixList.ItemsSource = fixes;
        // Only the general "run a command" is on offer: say so, rather than present an empty box as if it were the fix.
        NoFixNote.Visibility = fixes.Count > 0 && fixes.All(f => f.Id == "run-command") ? Visibility.Visible : Visibility.Collapsed;
        if (fixes.Count > 0) FixList.SelectedIndex = 0;
    }

    /// <summary>The run button is live only when the chosen fix has everything it needs.</summary>
    private void UpdateRunnable()
        => RunBtn.IsEnabled = Chosen is { } f && (f.ParamLabel is null || !string.IsNullOrWhiteSpace(ParamBox.Text));

    private void ParamBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateRunnable();

    public FixOption? Chosen => FixList.SelectedItem as FixOption;
    public string? Param => ParamPanel.Visibility == Visibility.Visible ? ParamBox.Text : null;

    private void FixList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var f = Chosen;
        if (f is null) { RunBtn.IsEnabled = false; return; }
        ParamPanel.Visibility = f.ParamLabel is null ? Visibility.Collapsed : Visibility.Visible;
        ParamLabel.Text = f.ParamLabel ?? "";
        ParamBox.Text = f.PrefilledParam ?? "";
        ParamBox.MinHeight = f.Id == "run-command" ? 120 : 32;
        var warn = f.Disruptive && _someoneActive;
        WarningText.Visibility = warn ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text = warn ? "Someone is actively using this PC. They get a 5-minute warning on screen, but unsaved work is at risk." : "";
        RunBtn.Content = f.Disruptive ? "Restart it" : "Run fix";
        // A restart someone is in the middle of using looks like what it is: the danger style, not the inviting one.
        RunBtn.Style = (Style)FindResource(warn ? "Ops.Danger" : "Ops.Primary");
        UpdateRunnable();
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!RunBtn.IsEnabled) return;   // Enter (IsDefault) while the box is still empty
        DialogResult = true;
    }
}
