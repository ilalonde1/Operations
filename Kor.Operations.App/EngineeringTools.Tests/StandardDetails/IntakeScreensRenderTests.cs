using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kor.Operations.StandardDetails;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// Builds each intake window and renders it to a PNG a person can open and look at.
///
/// WHY THIS IS A TEST AND NOT A ONE-OFF. A window that references a StaticResource its own
/// dictionary does not define compiles fine and throws the moment it is opened — that exact fault
/// shipped here before, which is why SheetComposerWindow carries the comment "a window cannot see
/// another window's resources, so define it here too (crashed on open otherwise)". Constructing and
/// rendering the window is the cheapest thing that catches it.
///
/// WHAT IT COVERS: that each window constructs, that every StaticResource it names resolves, and
/// that it lays out to a non-blank bitmap at its design size. The PNGs it writes are the artefact
/// for actually looking at the layout.
/// WHAT IT DOES NOT COVER: whether the result looks GOOD, anything behind the Loaded event (the
/// windows are deliberately never shown, so no data loads and no bridge is contacted), interaction,
/// and any state a screen only reaches after a user acts.
/// A same-class fault it would NOT catch: a control correctly laid out but painted the same colour
/// as its background — it renders, it is non-blank, and it is invisible.
/// </summary>
[Trait("Speed", "Slow")]
public sealed class IntakeScreensRenderTests
{
    [Fact]
    public void Every_intake_window_builds_and_renders()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "kor-intake-screens");
        Directory.CreateDirectory(outputDirectory);

        Exception? failure = null;
        var written = 0;

        // WPF demands STA and xUnit runs MTA, so the whole thing goes on its own thread.
        var thread = new Thread(() =>
        {
            try
            {
                // A bare Application carrying only the theme: constructing the real one would run
                // startup, DI and config, none of which a layout render needs.
                if (Application.Current is null)
                {
                    _ = new Application();
                }

                Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Kor.Operations.App;component/Themes/KorTheme.xaml", UriKind.Absolute),
                });

                // Repositories and the bridge client are constructed, never connected: none of them
                // opens anything until a method is called, and Loaded never fires on an unshown
                // window. So this touches no database and no Revit session.
                var promoter = new KorStandardsPromoterRepository("Server=.;Database=x;Integrated Security=true;");
                var catalogue = new KorStandardsReadRepository("Server=.;Database=x;Integrated Security=true;");
                var options = new StandardDetailsMasterPublishOptions(
                    @"\\example\AUTHORING\KOR-Standards-Authoring-R25.rvt",
                    @"\\example\MASTER\KOR-Standards-Master-R25.rvt",
                    @"\\example\bridge");
                var intake = new DetailIntake(new DrafterBridgeClient(options.BridgeRoot), options);

                written += Render(new AddDetailWindow(promoter, catalogue, intake, "lfinnigan@korstructural.com"),
                    Path.Combine(outputDirectory, "add-detail.png"));
                written += Render(new EditDetailWindow("KOR-D-00417", "BASEMENT WALL SECTION - 2 LEVELS, HOOK VERTS", "Concrete"),
                    Path.Combine(outputDirectory, "edit-detail.png"));
                written += Render(new RetireDetailWindow("KOR-D-00417", "BASEMENT WALL SECTION - 2 LEVELS, HOOK VERTS"),
                    Path.Combine(outputDirectory, "retire-detail.png"));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // A hard join, not an open-ended one: if a window ever does manage to raise a modal, this
        // fails with a message instead of wedging the suite the way an earlier full run did.
        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "Rendering the intake windows did not finish within two minutes.");
        Assert.True(failure is null, $"An intake window failed to build or render: {failure}");
        Assert.Equal(3, written);
    }

    /// <summary>
    /// Measure/Arrange/Render rather than Show: showing raises Loaded, and these windows load their
    /// data there — Add detail would go to the bridge and, on failure, raise a modal nothing can
    /// dismiss. Layout needs none of that.
    /// </summary>
    private static int Render(Window window, string path)
    {
        var width = double.IsNaN(window.Width) || window.Width <= 0 ? 1000 : window.Width;
        var height = double.IsNaN(window.Height) || window.Height <= 0 ? 700 : window.Height;

        // The window's CONTENT, not the window. A Window that has never been shown has no realized
        // visual of its own and renders blank — its content tree renders correctly. The chrome is
        // the operating system's anyway, so nothing about the layout under test is lost.
        var root = window.Content as FrameworkElement
                   ?? throw new InvalidOperationException($"{window.GetType().Name} has no FrameworkElement content to render.");

        // SizeToContent windows declare no Height, so measure to infinity and take what it asks for.
        var available = new Size(width, double.IsNaN(window.Height) || window.Height <= 0 ? double.PositiveInfinity : height);
        root.Measure(available);
        var desired = new Size(width, Math.Min(root.DesiredSize.Height, 4000));
        root.Arrange(new Rect(desired));
        root.UpdateLayout();

        var target = new RenderTargetBitmap((int)desired.Width, (int)Math.Max(desired.Height, 1), 96, 96, PixelFormats.Pbgra32);
        target.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        Assert.True(new FileInfo(path).Length > 4096, $"{Path.GetFileName(path)} rendered essentially empty.");
        return 1;
    }
}
