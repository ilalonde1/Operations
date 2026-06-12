#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Opportunities;
using Kor.Operations.Services;
using Kor.Opportunities.Data.BdReports.Generators;
using Microsoft.Web.WebView2.Core;
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
            // kor:// drill-down links in the preview route to the existing
            // detail surfaces; both events must be hooked because plain click
            // raises NavigationStarting while ctrl/middle-click raises
            // NewWindowRequested instead.
            Preview.CoreWebView2.NavigationStarting += Preview_NavigationStarting;
            Preview.CoreWebView2.NewWindowRequested += Preview_NewWindowRequested;
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

    // ===== kor:// drill-down routing =====
    //
    // Report previews carry kor://mpi/<id> and kor://org/<id> anchors
    // (KorReportLinks). Cancel the WebView2 navigation — kor:// is not a
    // registered OS scheme, so letting it through would surface Edge's
    // "open external app" failure path — and open the same detail surfaces
    // the BD dashboard drill-downs use (DashboardView.xaml.cs).

    private void Preview_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!KorReportLinks.TryParse(e.Uri, out var kind, out var id))
        {
            return; // NavigateToString content loads (about:blank / data:) — leave it alone
        }

        e.Cancel = true;
        // Fire-and-forget by design: NavigationStarting must return promptly,
        // and OpenKorLinkAsync owns its own error reporting.
        _ = OpenKorLinkAsync(kind, id);
    }

    private void Preview_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!KorReportLinks.TryParse(e.Uri, out var kind, out var id))
        {
            return;
        }

        e.Handled = true; // no popup browser window for kor:// targets
        _ = OpenKorLinkAsync(kind, id);
    }

    private async Task OpenKorLinkAsync(string kind, long id)
    {
        try
        {
            switch (kind)
            {
                case KorReportLinks.MpiKind:
                    var briefVm = AppServices.Get<PursuitBriefViewModel>();
                    await briefVm.LoadAsync(id, CancellationToken.None).ConfigureAwait(true);
                    new PursuitBriefWindow(briefVm) { Owner = this }.Show();
                    break;

                case KorReportLinks.OrgKind:
                    var dossierVm = AppServices.Get<OrgDossierViewModel>();
                    new OrgDossierWindow(dossierVm, id) { Owner = this }.Show();
                    break;
            }
        }
        catch (Exception ex)
        {
            // Async-void-equivalent context: an unhandled throw here would
            // crash the app. Surface the failure on the status bar instead.
            _vm.StatusMessage = $"Drill-down failed for {kind} {id}: {ex.Message}";
        }
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
