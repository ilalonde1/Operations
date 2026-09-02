#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace Kor.Operations.StandardDetails;

public partial class LinkDetailWindow : Window
{
    private readonly KorStandardsReadRepository _repo;

    public string? SelectedDetailNumber { get; private set; }
    public bool ClearRequested { get; private set; }

    internal LinkDetailWindow(KorStandardsReadRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadPaletteAsync(string.Empty);
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        await LoadPaletteAsync(SearchBox.Text?.Trim() ?? string.Empty);
    }

    private async Task LoadPaletteAsync(string query)
    {
        IReadOnlyList<PaletteDetailRow> rows;
        try
        {
            rows = await _repo.LoadPaletteDetailsAsync(query);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Standard Details - KorStandards Catalog", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        PaletteGrid.ItemsSource = rows;
    }

    private void LinkSelected_Click(object sender, RoutedEventArgs e)
    {
        if (PaletteGrid.SelectedItem is not PaletteDetailRow row)
        {
            MessageBox.Show(this, "Select a detail first.", "Standard Details - Link Detail", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (row.VariantsDiverge)
        {
            var result = MessageBox.Show(
                this,
                "This detail has divergent size variants. Approving it will set it human-confirmed but it will NOT become placeable until the divergence is resolved. Link anyway?",
                "Standard Details - Divergent Detail",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (result != MessageBoxResult.OK)
            {
                return;
            }
        }

        SelectedDetailNumber = row.DetailNumber;
        ClearRequested = false;
        DialogResult = true;
    }

    private void RemoveLink_Click(object sender, RoutedEventArgs e)
    {
        SelectedDetailNumber = null;
        ClearRequested = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
