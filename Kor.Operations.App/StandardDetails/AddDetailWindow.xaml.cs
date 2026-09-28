#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Kor.Operations.StandardDetails;

/// <summary>
/// The act the catalogue never had: "this drawing is now a KOR standard detail."
///
/// Order matters. The number is minted and the catalogue row written FIRST, then the model is
/// stamped. A failed stamp leaves a detail somebody can finish; a failed insert after a successful
/// stamp would leave a number written into a Revit view that the catalogue has never heard of, and
/// that is the state nobody can reason about.
/// </summary>
public partial class AddDetailWindow : Window
{
    // Listing ~1,100 views is two bridge round trips; the bridge only answers while Revit is open,
    // so a generous timeout here is the difference between "not running" and "still thinking".
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan StampTimeout = TimeSpan.FromSeconds(30);

    private readonly KorStandardsPromoterRepository _promoter;
    private readonly KorStandardsReadRepository _catalogue;
    private readonly DetailIntake _intake;
    private readonly string _actor;

    private IReadOnlyList<ViewInModel> _uncatalogued = Array.Empty<ViewInModel>();
    private bool _busy;

    /// <summary>The numbers minted in this sitting, so the caller can report and refresh.</summary>
    internal List<string> AddedDetailNumbers { get; } = [];

    internal AddDetailWindow(KorStandardsPromoterRepository promoter, KorStandardsReadRepository catalogue,
        DetailIntake intake, string actor)
    {
        InitializeComponent();
        _promoter = promoter ?? throw new ArgumentNullException(nameof(promoter));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _actor = string.IsNullOrWhiteSpace(actor) ? Environment.UserName : actor;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadViewsAsync();

    private async void Reload_Click(object sender, RoutedEventArgs e) => await LoadViewsAsync();

    private async System.Threading.Tasks.Task LoadViewsAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, "Asking the Revit session which views exist...");
        try
        {
            // The catalogue's own view ids first, because that read is milliseconds and it spares
            // the bridge from reporting 46 parameters each for a thousand views it already knows.
            var known = (await _catalogue.LoadCatalogueBindingAsync())
                .Where(x => x.ViewElementId.HasValue)
                .Select(x => x.ViewElementId!.Value)
                .ToHashSet();

            var snapshot = await _intake.ListDetailViewsAsync(known, ListTimeout);
            _uncatalogued = snapshot.Uncatalogued;

            ApplyFilter();
            SummaryText.Text = $"{snapshot.Views.Count} drawings in {snapshot.DocumentName}; "
                               + $"{snapshot.Views.Count - _uncatalogued.Count} are already standards.";
            SetStatus(_uncatalogued.Count == 0
                ? "Every drafting view in the model already carries a KOR-D number."
                : "Pick the drawing you want to make a standard.");
        }
        catch (Exception ex)
        {
            // The overwhelmingly likely cause is that Revit is not open on the bridge machine, so
            // say that first rather than making the gatekeeper decode a timeout.
            _uncatalogued = Array.Empty<ViewInModel>();
            ApplyFilter();
            SetStatus("Could not read the model: " + ex.Message);
            MessageBox.Show(this,
                "The Standard Details bridge did not answer." + Environment.NewLine + Environment.NewLine
                + ex.Message + Environment.NewLine + Environment.NewLine
                + "AUTHORING has to be open in the Revit session the bridge is watching before a detail can be added.",
                "Add a Standard Detail", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false, null);
            UpdateAddButton();
        }
    }

    private void ApplyFilter()
    {
        var needle = SearchBox.Text?.Trim() ?? "";
        var shown = string.IsNullOrEmpty(needle)
            ? _uncatalogued
            : _uncatalogued.Where(x => x.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();

        ViewsGrid.ItemsSource = shown;
        ListCountText.Text = shown.Count == _uncatalogued.Count
            ? $"{_uncatalogued.Count} not yet standards"
            : $"{shown.Count} of {_uncatalogued.Count} not yet standards";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ViewsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewsGrid.SelectedItem is ViewInModel view && string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            // Offered, not imposed: the view name is usually the right title and occasionally is a
            // drafting note. It stays editable and is only prefilled while the box is untouched.
            TitleBox.Text = view.Name.Trim();
        }

        UpdateAddButton();
    }

    private void ViewsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewsGrid.SelectedItem is ViewInModel)
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        }
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateAddButton();

    private void UpdateAddButton()
        => AddButton.IsEnabled = !_busy
                                 && ViewsGrid.SelectedItem is ViewInModel
                                 && !string.IsNullOrWhiteSpace(TitleBox.Text);

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || ViewsGrid.SelectedItem is not ViewInModel view)
        {
            return;
        }

        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            SetStatus("A title is required. It is what an engineer searches on.");
            return;
        }

        var (kind, isSheet, typeLabel) = SelectedType();
        var discipline = SelectedTag(DisciplineCombo) ?? "General";
        var basis = string.IsNullOrWhiteSpace(BasisBox.Text)
            ? $"Added in Operations Standard Details from view '{view.Name}'."
            : BasisBox.Text.Trim();

        SetBusy(true, $"Minting a number for {title}...");
        try
        {
            var request = new NewDetailRequest(title, discipline, kind, isSheet,
                _intake.DocumentName, view.Id, view.Name, view.Kind);

            var (ok, detailNumber, message) = await _promoter.AddDetailAsync(request, _actor, basis);
            if (!ok)
            {
                SetStatus(message);
                MessageBox.Show(this, message, "Add a Standard Detail", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AddedDetailNumbers.Add(detailNumber);

            // The catalogue row exists from here on. A stamp failure is reported, never silently
            // swallowed and never rolled back — the occurrence binds on the view's element id, so
            // the detail is correct either way; only the label in the model is missing.
            var (stamped, stampMessage) = await _intake.StampViewPrefixAsync(view.Id, detailNumber, StampTimeout);

            if (stamped)
            {
                SetStatus($"{detailNumber} added as {typeLabel.ToLowerInvariant()}, unverified. {stampMessage}");
                MessageBox.Show(this,
                    $"{detailNumber} — {title}" + Environment.NewLine + Environment.NewLine
                    + "The catalogue has it and the Revit view now carries the number." + Environment.NewLine + Environment.NewLine
                    + "Next: capture its drawing, approve it, then publish to MASTER.",
                    "Add a Standard Detail", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                SetStatus($"{detailNumber} added, but the Revit view was not stamped: {stampMessage}");
                MessageBox.Show(this,
                    $"{detailNumber} was added to the catalogue and is bound to the view you picked." + Environment.NewLine + Environment.NewLine
                    + "The View Prefix parameter on the Revit view was NOT set:" + Environment.NewLine
                    + stampMessage + Environment.NewLine + Environment.NewLine
                    + $"Set View Prefix to {detailNumber} on that view by hand, or run Reconcile once the model is reachable.",
                    "Add a Standard Detail", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // It is a standard now, so it leaves the candidate list.
            _uncatalogued = _uncatalogued.Where(x => x.Id != view.Id).ToList();
            ApplyFilter();
            TitleBox.Clear();
            BasisBox.Clear();
        }
        finally
        {
            SetBusy(false, null);
            UpdateAddButton();
        }
    }

    private (string Kind, bool IsSheet, string Label) SelectedType()
        => SelectedTag(TypeCombo) switch
        {
            "custom" => ("custom", false, "Custom detail"),
            "note-schedule" => ("general-note", true, "Note / schedule"),
            _ => ("typical", false, "Typical detail"),
        };

    private static string? SelectedTag(Selector combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = AddedDetailNumbers.Count > 0;
        Close();
    }

    private void SetBusy(bool busy, string? message)
    {
        _busy = busy;
        Cursor = busy ? Cursors.Wait : null;
        AddButton.IsEnabled = !busy && AddButton.IsEnabled;
        if (message is not null)
        {
            SetStatus(message);
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
