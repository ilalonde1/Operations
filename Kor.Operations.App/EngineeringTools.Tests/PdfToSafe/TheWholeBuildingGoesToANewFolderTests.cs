#nullable enable
using System;
using System.IO;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// THE WHOLE BUILDING GOES TO A NEW FOLDER (2026-10-20). The window's "Build whole building" runs StickfileRun, and
/// PdfOnlyBuild.Build DELETES the folder it is given before writing - so handed an existing model folder it would wipe
/// a model the engineer may already have sent. Every run goes to "&lt;pdf&gt; - ETABS model - &lt;date&gt; vN", N one past
/// the highest there for that PDF.
/// WHAT THIS COVERS: the next number skips gaps and ignores other PDFs' folders and folders not in the pattern; the
/// result never names an existing folder. WHAT IT DOES NOT: the build itself (StickfileRun, the same code the CLI
/// runs), or two windows building the same PDF in the same instant.
/// </summary>
public sealed class TheWholeBuildingGoesToANewFolderTests
{
    [Fact]
    public void TheNextFolderIsOnePastTheHighestAndNeverOneThatExists()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"kor-whole-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string pdf = Path.Combine(dir, "143 E 3rd Hotel markup.pdf");
            File.WriteAllText(pdf, "");
            Assert.EndsWith(" v1", PdfToSafeWindow.NextModelFolder(pdf));

            Directory.CreateDirectory(Path.Combine(dir, "143 E 3rd Hotel markup - ETABS model - 2026-10-19 v1"));
            Directory.CreateDirectory(Path.Combine(dir, "143 E 3rd Hotel markup - ETABS model - 2026-10-19 v3 SUPERSEDED (floor spike)"));
            Directory.CreateDirectory(Path.Combine(dir, "Another job - ETABS model - 2026-10-19 v9"));
            Directory.CreateDirectory(Path.Combine(dir, "143 E 3rd Hotel markup - ETABS model - notes"));

            string next = PdfToSafeWindow.NextModelFolder(pdf);
            Assert.Equal($"143 E 3rd Hotel markup - ETABS model - {DateTime.Now:yyyy-MM-dd} v4", Path.GetFileName(next));
            Assert.False(Directory.Exists(next));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
