#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.Services;
using Microsoft.Win32;

namespace Kor.Operations.App.BusinessDevelopment.Reports;

public partial class BdReportsWindow : Window
{
    private readonly BdReportsViewModel _vm;
    private bool _aiRegistered;
    private bool _previewReady;
    private CancellationTokenSource? _previewCts;

    public BdReportsWindow(BdReportsViewModel vm)
    {
        InitializeComponent();
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        DataContext = _vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Register(_vm);
            _aiRegistered = true;
        }

        try
        {
            await HeaderLoader.ApplyAsync(HeaderBar).ConfigureAwait(true);
        }
        catch
        {
            // Header identity is cosmetic and should not block the dashboard.
        }

        try
        {
            await Preview.EnsureCoreWebView2Async().ConfigureAwait(true);
            _previewReady = true;
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Preview pane unavailable (WebView2): {ex.Message}. DOCX export still works after selecting a sector.";
        }

        await _vm.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        if (SectorList.SelectedItem is null && _vm.Sectors.Count > 0)
        {
            SectorList.SelectedIndex = 0; // triggers SectorList_SelectionChanged
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        if (_aiRegistered)
        {
            AppServices.Get<AppAiContextBuilder>().Unregister(_vm);
            _aiRegistered = false;
        }

        base.OnClosed(e);
    }

    private async void SectorList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        await RegeneratePreviewAsync().ConfigureAwait(true);
    }

    private async void AnalyticalList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_vm.SelectedAnalytical is null)
        {
            return; // selection was cleared by picking a sector
        }

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();

        var html = await _vm.BuildAnalyticalPreviewAsync(_previewCts.Token).ConfigureAwait(true);
        TryShowPreview(html);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        // LoadAsync clears the Sectors collection, which nulls SelectedSector
        // through the TwoWay binding — capture and restore so Refresh keeps
        // the user's place instead of stranding the old preview.
        var sectorKey = _vm.SelectedSector?.Key;
        var hadAnalytical = _vm.SelectedAnalytical is not null;

        await _vm.LoadAsync(CancellationToken.None).ConfigureAwait(true);

        if (sectorKey is not null)
        {
            var restored = _vm.Sectors.FirstOrDefault(s => s.Key == sectorKey);
            if (restored is not null)
            {
                SectorList.SelectedItem = restored; // SelectionChanged regenerates
                return;
            }
        }

        if (hadAnalytical)
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = new CancellationTokenSource();
            var html = await _vm.BuildAnalyticalPreviewAsync(_previewCts.Token).ConfigureAwait(true);
            TryShowPreview(html);
        }
    }

    private async Task RegeneratePreviewAsync()
    {
        if (_vm.SelectedSector is null)
        {
            return;
        }

        // A new selection supersedes any in-flight generation.
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();

        var html = await _vm.BuildPreviewAsync(_previewCts.Token).ConfigureAwait(true);
        TryShowPreview(html);
    }

    // NavigateToString can throw (runtime failures, ~2MB content cap) and these
    // call sites are async void handlers — an unhandled throw here would crash
    // the app (adversarial review M3).
    private void TryShowPreview(string? html)
    {
        if (html is null || !_previewReady)
        {
            return;
        }

        try
        {
            Preview.NavigateToString(html);
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Preview render failed: {ex.Message}. Save DOCX still works.";
        }
    }

    private void SaveDocx_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save BD Report",
            Filter = "Word Document|*.docx",
            FileName = _vm.SuggestedDocxFileName,
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dlg.FileName, _vm.RenderCurrentDocx());
            _vm.LogDocxExportBestEffort($"Saved as {Path.GetFileName(dlg.FileName)}");
            _vm.StatusMessage = $"Saved {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"DOCX export failed:\n{ex.Message}", "BD Reports", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
