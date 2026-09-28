#nullable enable
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Kor.Operations.StandardDetails;

/// <summary>
/// Changing a detail's title or discipline. Kind and is-sheet already had setters; these two did
/// not, so until migration 102 a typo in either was an sa job.
/// </summary>
public partial class EditDetailWindow : Window
{
    private readonly string _originalTitle;
    private readonly string _originalDiscipline;

    /// <summary>Null when unchanged, so the procedure leaves the stored value alone.</summary>
    internal string? NewTitle { get; private set; }

    /// <summary>Null when unchanged, "" to clear it — the procedure distinguishes the two.</summary>
    internal string? NewDiscipline { get; private set; }

    internal string Why { get; private set; } = string.Empty;

    internal EditDetailWindow(string detailNumber, string title, string discipline)
    {
        InitializeComponent();
        _originalTitle = title ?? string.Empty;
        _originalDiscipline = discipline ?? string.Empty;

        WindowTitleText.Text = $"Edit {detailNumber}";
        TitleBox.Text = _originalTitle;
        SelectDiscipline(_originalDiscipline);

        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void SelectDiscipline(string discipline)
    {
        var match = DisciplineCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(x => string.Equals(x.Tag?.ToString() ?? "", discipline, StringComparison.OrdinalIgnoreCase));

        // An unrecognised stored value (there are 3 details with no discipline at all) lands on
        // "(none)" rather than silently picking Concrete.
        DisciplineCombo.SelectedItem = match ?? DisciplineCombo.Items.OfType<ComboBoxItem>().Last();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show(this, "A title is required.", "Standard Details — Edit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var discipline = (DisciplineCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

        // Only send what actually changed: the procedure journals a row per field it changes, and a
        // log that fills with no-ops is a log nobody reads.
        NewTitle = string.Equals(title, _originalTitle, StringComparison.Ordinal) ? null : title;
        NewDiscipline = string.Equals(discipline, _originalDiscipline, StringComparison.OrdinalIgnoreCase) ? null : discipline;

        if (NewTitle is null && NewDiscipline is null)
        {
            DialogResult = false;
            return;
        }

        Why = WhyBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
