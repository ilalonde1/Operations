#nullable enable
using System;
using System.IO;
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

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _vm.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        await RegeneratePreviewAsync().ConfigureAwait(true);
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
        if (html is not null && _previewReady)
        {
            Preview.NavigateToString(html);
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
            _vm.StatusMessage = $"Saved {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"DOCX export failed:\n{ex.Message}", "BD Reports", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
