#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.Opportunities;

/// <summary>What the user chose on the possible-duplicate prompt.</summary>
public enum DuplicateChoice
{
    Cancel,
    OpenExisting,
    SaveAnyway,
}

/// <summary>
/// Manual-entry duplicate guard prompt (2026-07-07). Shows the possible
/// existing matches for a new opportunity and lets the user open one instead,
/// save as new anyway, or go back. Never blocks a genuine new entry — it's a
/// confirm, not a wall.
/// </summary>
public partial class OpportunityDuplicateDialog : Window
{
    public OpportunityDuplicateDialog(string proposedName, IReadOnlyList<OpportunityDuplicateCandidate> matches)
    {
        InitializeComponent();
        HeaderText.Text = matches.Count == 1
            ? $"This looks like an opportunity we already have. Open it instead of creating “{proposedName}”?"
            : $"This looks like {matches.Count} opportunities we already have. Open one instead of creating “{proposedName}”?";
        MatchGrid.ItemsSource = matches.Select(m => new Row(m)).ToList();
        MatchGrid.SelectedIndex = 0;
    }

    /// <summary>The user's decision.</summary>
    public DuplicateChoice Choice { get; private set; } = DuplicateChoice.Cancel;

    /// <summary>The chosen existing opportunity key (only when <see cref="Choice"/> is OpenExisting).</summary>
    public string? SelectedKey { get; private set; }

    private void OpenExisting_Click(object sender, RoutedEventArgs e) => ChooseOpen();

    private void MatchGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => ChooseOpen();

    private void ChooseOpen()
    {
        if (MatchGrid.SelectedItem is not Row row)
        {
            MessageBox.Show(this, "Pick which existing opportunity to open.", "Possible duplicate",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Choice = DuplicateChoice.OpenExisting;
        SelectedKey = row.OpportunityKey;
        DialogResult = true;
    }

    private void SaveAnyway_Click(object sender, RoutedEventArgs e)
    {
        Choice = DuplicateChoice.SaveAnyway;
        DialogResult = true;
    }

    /// <summary>Display projection of one candidate.</summary>
    private sealed class Row
    {
        private static readonly Brush HighBrush = Frozen(0xC1, 0x1E, 0x1E);   // red
        private static readonly Brush MediumBrush = Frozen(0xE5, 0xA8, 0x00); // amber

        private readonly OpportunityDuplicateCandidate _m;

        public Row(OpportunityDuplicateCandidate m) => _m = m;

        public string OpportunityKey => _m.OpportunityKey;
        public string Name => _m.Name;
        public string BuyerName => _m.BuyerName;
        public string ConfidenceLabel => _m.Confidence == DuplicateConfidence.High ? "Likely" : "Possible";
        public Brush ConfidenceBrush => _m.Confidence == DuplicateConfidence.High ? HighBrush : MediumBrush;
        public string PursuitDisplay => _m.HasPursuit ? "has pursuit" : "";

        public string StatusDisplay => Enum.IsDefined(typeof(OpportunityStatus), _m.Status)
            ? ((OpportunityStatus)_m.Status).ToString()
            : _m.Status.ToString();

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
