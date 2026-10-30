using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Kor.Operations.App.Options;
using Kor.Operations.Services;
using Kor.Operations.StandardDetails;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// Opens the real Standard Details window against the LIVE catalogue, switches to the Sheets tab, selects
/// the first sheet, and renders each state to a PNG to look at (%TEMP%\kor-standard-details-live).
///
/// WHY: the window loads its data on Loaded, which IntakeScreensRenderTests deliberately never raises,
/// and a session that cannot read the screen (30 Oct 2026: CopyFromScreen "handle is invalid", PrintWindow
/// blank) has no other way to see the catalogue as an engineer does. Board row 103: Jim could not find
/// the cover sheets; this shows whether the Sheets tab now lists them, grouped, with their drawing.
///
/// WHAT IT COVERS: that the window opens on the installed app's own connections (C:\KOR-Operations, read
/// only - nothing is written), lists details, switches to Sheets and lists exactly the five collections
/// with their sheet counts (an empty collection is never listed: groups come from rows), shows S1.03's
/// stored drawing and its 30 recorded details, and that clicking the first of them lands on that detail
/// in the Details tab. WHAT IT DOES NOT COVER: whether the layout looks good (look at the PNGs), scrolling
/// (a virtualised list renders only what fits), the other 56 sheets' lists, or Approve/Reject/Open PDF.
/// A same-class fault it would NOT catch: a sheet whose OnSheet records are missing in the database shows
/// "No catalogued details" correctly here - the harness checks the window against the data, not the data
/// against Nelson's master (detail_drawings.py checksheets/order do that).
/// Skipped (passes without doing anything) on a machine without the installed app's config.
/// </summary>
[Trait("Speed", "Slow")]
public sealed class StandardDetailsLiveRenderTests
{
    private const string InstalledConfig = @"C:\KOR-Operations\Kor.Operations.App.dll.config";

    [Fact]
    public void Sheets_tab_lists_the_cover_sheets_live()
    {
        if (!File.Exists(InstalledConfig))
        {
            return;
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), "kor-standard-details-live");
        Directory.CreateDirectory(outputDirectory);
        Exception? failure = null;
        string summary = "";

        var thread = new Thread(() =>
        {
            try
            {
                // the window's loads are async: without the dispatcher's context on this thread their continuations
                // resume on the thread pool and touch the UI from there (first run, 30 Oct: the testhost crashed in
                // SetActivityMessage). The app's UI thread has this context; a bare thread does not.
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                if (Application.Current is null)
                {
                    _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }

                Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Kor.Operations.App;component/Themes/KorTheme.xaml", UriKind.Absolute),
                });

                AppServices.Initialize(BuildServices(XDocument.Load(InstalledConfig)));

                var window = new StandardDetailsWindow
                {
                    Left = -20000, Top = -20000, Width = 1600, Height = 1000,
                    ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual,
                };
                window.Show();
                var grid = window.DocumentsGrid;

                Pump(() => grid.Items.Count > 0, TimeSpan.FromSeconds(60));
                var details = grid.Items.Count;
                Render(window, Path.Combine(outputDirectory, "1-details.png"));

                window.SheetsTab.IsChecked = true;
                Pump(() => grid.Items.Count > 0 && grid.Items.Count != details, TimeSpan.FromSeconds(30));
                var sheets = grid.Items.Cast<object>().ToList();
                var groups = sheets.Select(x => (string?)x.GetType().GetProperty("GroupName")?.GetValue(x) ?? "")
                    .GroupBy(x => x).Select(g => $"{g.Key} {g.Count()}").ToList();
                Render(window, Path.Combine(outputDirectory, "2-sheets.png"));

                // S1.03 carries 30 details on Nelson's master: its list must show them
                var s103 = sheets.First(x => ((string?)x.GetType().GetProperty("Title")?.GetValue(x) ?? "").StartsWith("S1.03 "));
                grid.SelectedItem = s103;
                grid.ScrollIntoView(s103);
                Pump(() => window.PreviewImage.Visibility == Visibility.Visible && window.PreviewImage.Source is not null
                           && window.SheetDetailsPanel.Visibility == Visibility.Visible, TimeSpan.FromSeconds(30));
                var onSheet = window.SheetDetailsWrap.Children.OfType<Button>().ToList();
                var preview = window.PreviewImage.Visibility == Visibility.Visible && window.PreviewImage.Source is not null;
                Render(window, Path.Combine(outputDirectory, "3-sheet-S1.03.png"));

                // a click on the first detail jumps to it in Details
                var target = (string)onSheet[0].Tag;
                onSheet[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(() => window.DetailsTab.IsChecked == true
                           && (string?)grid.SelectedItem?.GetType().GetProperty("DetailNumber")?.GetValue(grid.SelectedItem) == target,
                     TimeSpan.FromSeconds(30));
                var landed = (string?)grid.SelectedItem?.GetType().GetProperty("DetailNumber")?.GetValue(grid.SelectedItem);
                Render(window, Path.Combine(outputDirectory, "4-jumped-to-detail.png"));

                summary = $"details {details}; sheets {sheets.Count} in {groups.Count} collections: {string.Join("; ", groups)}; " +
                          $"preview shown {preview}; S1.03 lists {onSheet.Count} details; jump to {target} landed on {landed}; " +
                          $"details tab {window.DetailsTab.IsChecked}";
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(3)), "The live Standard Details render did not finish within three minutes.");
        Assert.True(failure is null, $"The live Standard Details render failed: {failure}");
        File.WriteAllText(Path.Combine(outputDirectory, "summary.txt"), summary);
        // exactly the five collections, each with sheets in it: an empty collection is never listed
        Assert.Contains("sheets 57 in 5 collections: Concrete tower - D 11; Concrete tower - E 8; Concrete tower - E1 8; " +
                        "Wood frame - D 17; Wood frame - E1 13;", summary);
        Assert.Contains("preview shown True", summary);
        Assert.Contains("S1.03 lists 30 details", summary);
        Assert.Matches(@"jump to (KOR-D-\d{5}) landed on \1; details tab True", summary);
    }

    /// <summary>The three option sets StandardDetailsWindow reads on Loaded, from the installed app's own
    /// .config (the testhost has no App.config of the app's) - the same keys CompositionHelpers reads.</summary>
    private static IServiceProvider BuildServices(XDocument config)
    {
        string Conn(string name) => config.Descendants("connectionStrings").Elements("add")
            .FirstOrDefault(x => (string?)x.Attribute("name") == name)?.Attribute("connectionString")?.Value ?? "";
        string Setting(string key) => config.Descendants("appSettings").Elements("add")
            .FirstOrDefault(x => (string?)x.Attribute("key") == key)?.Attribute("value")?.Value ?? "";

        return new ServiceCollection()
            .AddSingleton(new DatabaseOptions
            {
                KorStandardsDb = Conn("KorStandardsDb"),
                KorTransmittalsDb = Conn("KorTransmittalsDb"),
                // the promoter (write) login is left out on purpose: this harness only reads
            })
            .AddSingleton(new StorageOptions
            {
                ProjectsRoot = Setting(AppConfigKeys.ProjectsRoot),
                StandardDetailsFileStorageRootPath = Setting(AppConfigKeys.StandardDetailsFileStorageRootPath),
                StandardDetailsAuthoringPath = Setting(AppConfigKeys.StandardDetailsAuthoringPath),
                StandardDetailsMasterPath = Setting(AppConfigKeys.StandardDetailsMasterPath),
                StandardDetailsBridgeRoot = Setting(AppConfigKeys.StandardDetailsBridgeRoot),
                StandardDetailsPartImageRoot = Setting(AppConfigKeys.StandardDetailsPartImageRoot),
            })
            .AddSingleton(new UserOptions())
            .BuildServiceProvider();
    }

    /// <summary>Runs the dispatcher until the condition holds or the time is up - the window's loads are
    /// async continuations on this thread, so nothing completes unless the dispatcher runs.</summary>
    private static void Pump(Func<bool> done, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (!done() && DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(50);
        }
        // one more pass so a load that just finished has laid out
        var last = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => last.Continue = false));
        Dispatcher.PushFrame(last);
    }

    private static void Render(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var target = new RenderTargetBitmap((int)Math.Max(root.ActualWidth, 1), (int)Math.Max(root.ActualHeight, 1), 96, 96, PixelFormats.Pbgra32);
        target.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
