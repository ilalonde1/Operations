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
        if (fixes.Count > 0) FixList.SelectedIndex = 0;
    }

    public FixOption? Chosen => FixList.SelectedItem as FixOption;
    public string? Param => ParamPanel.Visibility == Visibility.Visible ? ParamBox.Text : null;

    private void FixList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var f = Chosen;
        RunBtn.IsEnabled = f is not null;
        if (f is null) return;
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
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (Chosen is { ParamLabel: not null } && string.IsNullOrWhiteSpace(ParamBox.Text))
        {
            MessageBox.Show(this, $"{Chosen.ParamLabel} is needed.", "Fix", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
