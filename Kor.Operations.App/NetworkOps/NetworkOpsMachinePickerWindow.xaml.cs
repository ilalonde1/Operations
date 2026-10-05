#nullable enable
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Kor.Operations.App.NetworkOps;

/// <summary>Pick which machines an issue's disposition (acknowledge/snooze) applies to, when it is open on more than one.
/// A To-clear issue groups the same problem across machines; acknowledging "all N" is not always right -- one PC's drive
/// is handled while another's still needs attention. All are checked by default; OK returns the chosen ones by their index
/// in the input order.</summary>
public partial class NetworkOpsMachinePickerWindow : Window
{
    public sealed class Pick : INotifyPropertyChanged
    {
        public required string Name { get; init; }
        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked == value) return; _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<Pick> _picks;

    /// <param name="verb">"Acknowledge" or "Snooze" -- the action being applied.</param>
    /// <param name="issueTitle">The issue, shown as the subtitle.</param>
    /// <param name="machineNames">The machines the issue is open on, in the order the caller will map indexes back to.</param>
    public NetworkOpsMachinePickerWindow(string verb, string issueTitle, IReadOnlyList<string> machineNames)
    {
        InitializeComponent();
        Title = verb;
        HeaderText.Text = $"{verb} on which machines?";
        SubText.Text = issueTitle;
        _picks = new ObservableCollection<Pick>(machineNames.Select(n => new Pick { Name = n }));
        MachineList.ItemsSource = _picks;
    }

    /// <summary>The indexes (in the input order) of the machines left checked. Empty until OK.</summary>
    public IReadOnlyList<int> SelectedIndexes { get; private set; } = [];

    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var p in _picks) p.IsChecked = true; }

    private void ClearAll_Click(object sender, RoutedEventArgs e) { foreach (var p in _picks) p.IsChecked = false; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedIndexes = _picks.Select((p, i) => (p, i)).Where(x => x.p.IsChecked).Select(x => x.i).ToList();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
