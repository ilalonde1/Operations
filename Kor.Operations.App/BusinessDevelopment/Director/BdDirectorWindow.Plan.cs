#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Plan;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>
/// The Plan tab: the operating plan drawn by OperatingPlanPage into a WebView2. kor://plan/print prints this same page to
/// PDF, kor://plan/decide/{id} records Ian's decision on a goal, and every other kor:// link opens through KorDeepLink, the
/// same door the 6 a.m. email and the BD reports use.
/// </summary>
public partial class BdDirectorWindow
{
    private bool _planReady;
    private bool _planBusy;
    private bool _planShown;

    private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles up from every list inside the tabs; only the tab strip itself counts.
        if (!ReferenceEquals(e.OriginalSource, Tabs) || !ReferenceEquals(Tabs.SelectedItem, PlanTab) || _planShown) return;
        await LoadPlanAsync().ConfigureAwait(true);
    }

    private async Task LoadPlanAsync()
    {
        if (_planBusy) return;
        _planBusy = true;
        PlanStatus.Visibility = Visibility.Visible;
        PlanStatus.Text = "Opening the plan…";
        try
        {
            if (!_planReady)
            {
                await PlanView.EnsureCoreWebView2Async().ConfigureAwait(true);
                PlanView.CoreWebView2.NavigationStarting += PlanView_NavigationStarting;
                PlanView.CoreWebView2.NewWindowRequested += PlanView_NewWindowRequested;
                _planReady = true;
            }

            var html = await OperatingPlanPage.BuildAsync(AppServices.Get<IOperatingPlanStore>(), AppServices.Get<IPursuitTouchstoneStore>(),
                (from, ct) => AppServices.Get<SqlImpactStore>().ListAsync(from, ct), AppServices.Get<IBdRhythmStore>(),
                DateTimeOffset.Now, _lifetime.Token).ConfigureAwait(true);
            PlanView.NavigateToString(html);
            _planShown = true;
            PlanStatus.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { /* Window closed. */ }
        catch (Exception ex)
        {
            Log.Warning(ex, "The operating plan could not be drawn.");
            PlanStatus.Text = "The plan could not be opened: " + ex.Message;
        }
        finally { _planBusy = false; }
    }

    private void PlanView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var uri = e.Uri ?? "";
        // The page itself arrives as about:blank or a data: URI from NavigateToString; anything else stays inside the app.
        if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true;
        if (!uri.StartsWith("kor://", StringComparison.OrdinalIgnoreCase)) return;
        _ = OpenPlanLinkAsync(uri);
    }

    private void PlanView_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.Uri?.StartsWith("kor://", StringComparison.OrdinalIgnoreCase) == true) _ = OpenPlanLinkAsync(e.Uri);
    }

    private async Task OpenPlanLinkAsync(string uri)
    {
        try
        {
            if (string.Equals(uri.TrimEnd('/'), OperatingPlanHtml.PrintLink, StringComparison.OrdinalIgnoreCase))
                await PrintPlanAsync().ConfigureAwait(true);
            else if (OperatingPlanHtml.TryParseDecide(uri, out var goalId))
                await DecideGoalAsync(goalId).ConfigureAwait(true);
            else
                await KorDeepLink.OpenAsync(uri).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Plan link {Uri} failed.", uri);
            SetRhythmStatus("That link could not be opened: " + ex.Message);
        }
    }

    /// <summary>Prints exactly what the tab shows; the page's print layout runs all four views and every stage card.</summary>
    private async Task PrintPlanAsync()
    {
        if (!_planReady || PlanView.CoreWebView2 is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = OperatingPlanHtml.PdfFileName(DateTimeOffset.Now),
            Filter = "PDF (*.pdf)|*.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dlg.ShowDialog(this) != true) return;
        var settings = PlanView.CoreWebView2.Environment.CreatePrintSettings();
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        var ok = await PlanView.CoreWebView2.PrintToPdfAsync(dlg.FileName, settings).ConfigureAwait(true);
        SetRhythmStatus(ok ? "Saved the plan as " + Path.GetFileName(dlg.FileName) + "." : "The plan PDF could not be saved.");
    }

    /// <summary>Turns a recommended goal into a decided one, as written; the plan records who and when.</summary>
    private async Task DecideGoalAsync(int goalId)
    {
        var store = AppServices.Get<IOperatingPlanStore>();
        var plan = await store.LoadAsync(_lifetime.Token).ConfigureAwait(true);
        var goal = plan.Goals.FirstOrDefault(g => g.Id == goalId);
        if (goal is null) return;
        var target = goal.TargetValue is { } t && PlanMetrics.Find(goal.Metric) is { } m ? PlanMetrics.Format(m.Unit, t) : goal.TargetText;
        var answer = MessageBox.Show(this,
            $"Decide this goal as written?\n\n{goal.Title}\nTarget: {target}\n\nIt stops being a recommendation, and the plan records that you decided it.",
            "Decide goal", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        if (await store.DecideGoalAsync(goalId, null, CurrentActor(), _lifetime.Token).ConfigureAwait(true))
        {
            SetRhythmStatus("Decided: " + goal.Title + ".");
            await LoadPlanAsync().ConfigureAwait(true);
        }
    }
}
