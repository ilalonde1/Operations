using System.Runtime.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.Architecture.Tests;

/// <summary>
/// THE DRAWING HALF, COVERED LIKE THE REST OF IT.
///
/// This renders the real model to a scratch directory and checks what came out. It is a SMOKE test,
/// not a pixel comparison — the thing worth catching is a page that silently stops being drawn, a
/// COM formula that stops binding, or an export that writes a 200-byte file, and all three show up
/// in the page list and the file sizes.
///
/// It SKIPS when Visio is not installed rather than failing, because the extractor is useful on a
/// machine with no Office and the tests should still run there. The skip says so out loud, so a
/// green run on a build agent is never mistaken for coverage it does not have.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VisioRendererTests
{
    private readonly ITestOutputHelper _out;
    public VisioRendererTests(ITestOutputHelper output) => _out = output;

    private static bool VisioInstalled => Type.GetTypeFromProgID("Visio.Application") is not null;

    [Fact]
    public void EveryPageIsDrawnAndExported()
    {
        if (!VisioInstalled)
        {
            _out.WriteLine("SKIPPED: Visio is not installed on this machine.");
            return;
        }

        var model = Extractor.Extract(ExtractorTests.RepoRootForTests());
        string dir = Path.Combine(Path.GetTempPath(), "archmap-test-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            var result = VisioRenderer.Render(model, dir);

            foreach (string note in result.Notes) _out.WriteLine(note);

            // The page set is the contract. If one stops being drawn, that is the failure.
            var expected = new[]
            {
                "Application", "Drawing-intake", "Matrix-dependencies", "Matrix-formats",
                "CLI-verbs", "Duplication", "Master-matrix", "Nooks-and-crannies",
                "Relationships", "Recipes",
            };
            var actual = result.PngPaths.Select(Path.GetFileNameWithoutExtension).ToList();
            foreach (string page in expected)
                Assert.Contains(actual, a => a!.EndsWith(page, StringComparison.Ordinal));
            Assert.Equal(expected.Length, result.PngPaths.Count);

            // A page that draws nothing still exports — as a few hundred bytes of white.
            Assert.True(new FileInfo(result.VsdxPath).Length > 50_000,
                "the .vsdx is smaller than a document holding ten drawn pages could be");
            foreach (string png in result.PngPaths)
            {
                long size = new FileInfo(png).Length;
                Assert.True(size > 4096, $"{Path.GetFileName(png)} is {size} bytes — nothing was drawn on it");
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ItSaysSoRatherThanCrashingWhenVisioIsAbsent()
    {
        // The model is the durable artefact and is written before any of this runs. A machine with no
        // Visio should get a sentence, not a stack trace — and Program returns 3 rather than 0 so a
        // scripted run still knows the drawing did not happen.
        if (VisioInstalled)
        {
            _out.WriteLine("Visio IS installed here, so the absent path cannot be exercised; " +
                           "the guard is a null check on Type.GetTypeFromProgID in VisioRenderer.Render.");
            return;
        }

        var model = Extractor.Extract(ExtractorTests.RepoRootForTests());
        var ex = Assert.Throws<InvalidOperationException>(() => VisioRenderer.Render(model, Path.GetTempPath()));
        Assert.Contains("Visio is not installed", ex.Message, StringComparison.Ordinal);
    }
}
