using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// Builds the PDF-to-SAFE window and renders it to a PNG a person can open and look at
/// (%TEMP%\kor-pdftosafe-screens\pdftosafe-window.png). Written 2026-10-20 when the AI panel and the Rerun AI button
/// came out of the window: a removed control still named in the code-behind, or a StaticResource only it carried, shows
/// up here and not at compile time. Same method as IntakeScreensRenderTests.
/// WHAT IT COVERS: that the window constructs, that every StaticResource it names resolves, and that it lays out to a
/// non-blank bitmap at its design size, with no PDF loaded. WHAT IT DOES NOT: anything after a PDF loads (the colour
/// grid, the preview, the opening outlines), whether it looks GOOD, interaction. A same-class fault it would NOT catch:
/// a control laid out but painted the colour of its background.
/// </summary>
public sealed class PdfToSafeWindowRenderTests
{
    [Fact]
    public void ThePdfToSafeWindowBuildsAndRenders()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "kor-pdftosafe-screens");
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "pdftosafe-window.png");

        Exception? failure = null;

        // WPF demands STA and xUnit runs MTA, so the whole thing goes on its own thread.
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    _ = new Application();
                }
                Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Kor.Operations.App;component/Themes/KorTheme.xaml", UriKind.Absolute),
                });

                var window = new PdfToSafeWindow(NullLogger<PdfToSafeWindow>.Instance);
                var root = window.Content as FrameworkElement
                           ?? throw new InvalidOperationException("PdfToSafeWindow has no FrameworkElement content to render.");
                var size = new Size(window.Width, window.Height);
                root.Measure(size);
                root.Arrange(new Rect(size));
                root.UpdateLayout();

                var target = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                target.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(target));
                using var stream = File.Create(path);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "Rendering the PDF-to-SAFE window did not finish within two minutes.");
        Assert.True(failure is null, $"The PDF-to-SAFE window failed to build or render: {failure}");
        Assert.True(new FileInfo(path).Length > 4096, "pdftosafe-window.png rendered essentially empty.");
    }
}
