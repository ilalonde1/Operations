using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public class PublishExplainersTests
{
    [Fact]
    public void ExplainersDoNotTravelToJobsTheyDoNotDescribe()
    {
        string repo = NewFolder();
        Directory.CreateDirectory(Path.Combine(repo, "docs"));
        File.WriteAllText(Path.Combine(repo, "docs", "KOR-DxfToEtabs-dossier.html"),
            "<html><body>31138 model from drawings</body></html>");

        string modelFolder = NewFolder();
        var result = PublishExplainers.Evaluate(new PublishExplainersRequest(
            "31168",
            modelFolder,
            repo,
            NewFolder(),
            Path.Combine(modelFolder, "31168-FROM-DRAWINGS.e2k"),
            Path.Combine(modelFolder, "31168-FROM-DRAWINGS-report.txt"),
            E2kModelContents.Empty,
            SkipDossier: false,
            IsVariant: false));

        Assert.Empty(result.ToCopy);
        Assert.Null(result.Refused);
        Assert.Contains(result.Warnings, w => w.Contains("not 31168", StringComparison.Ordinal));
    }

    [Fact]
    public void SkipDossierWithdrawsExistingExplainers()
    {
        string repo = NewFolder();
        string modelFolder = NewFolder();

        var result = PublishExplainers.Evaluate(new PublishExplainersRequest(
            "31168",
            modelFolder,
            repo,
            NewFolder(),
            Path.Combine(modelFolder, "31168-FROM-DRAWINGS.e2k"),
            Path.Combine(modelFolder, "31168-FROM-DRAWINGS-report.txt"),
            E2kModelContents.Empty,
            SkipDossier: true,
            IsVariant: false));

        Assert.Empty(result.ToCopy);
        Assert.Contains(Path.Combine(modelFolder, "KOR-Model-From-Drawings-DOSSIER.pdf"), result.ToWithdraw);
        Assert.Contains(Path.Combine(modelFolder, "KOR-Model-From-Drawings-READ-THIS-FIRST.pdf"), result.ToWithdraw);
    }

    // The gate exists to say "the code that builds models moved on and this description did not".
    [Fact]
    public void ChangingGeometryCodeMakesTheExplainersStale()
    {
        string repo = NewRepoWithDossierNaming31168(out string modelFolder);
        TouchSource(repo, "DxfFloodFillPlateDetector.cs", DateTime.Now);

        var result = Evaluate(repo, modelFolder);

        Assert.NotNull(result.Refused);
        Assert.Contains("STALE", result.Refused!, StringComparison.Ordinal);
        // Named by the artifact an engineer opens, which is what actually travels to the job folder.
        Assert.Contains("KOR-DxfToEtabs-web.pdf", result.Refused!, StringComparison.Ordinal);
    }

    // ...but publishing moved into that same folder on 31 August, and finding a job folder or
    // copying a PDF cannot change a count, an outline or a storey. Before the port these files were
    // PowerShell, outside the watched directory. A gate that fires on them is one people re-render
    // past without reading, which is how it stops catching the thing it was written for.
    [Fact]
    public void ChangingTheDeliveryPipelineDoesNotMakeTheExplainersStale()
    {
        string repo = NewRepoWithDossierNaming31168(out string modelFolder);
        foreach (string file in PublishExplainers.DeliveryPipelineFiles)
            TouchSource(repo, file, DateTime.Now);

        var result = Evaluate(repo, modelFolder);

        // Not "publishes regardless" -- the claims gate still reads every number in the dossier and
        // still has its own say. Only the staleness reason must be absent.
        Assert.DoesNotContain("STALE", result.Refused ?? string.Empty, StringComparison.Ordinal);
    }

    // The Edge-cached "File not found" PDF shipped because the source was right and the artifact
    // was not. Nothing checked this direction until 31 August.
    [Fact]
    public void EditingTheProseWithoutReRenderingIsRefused()
    {
        string repo = NewRepoWithDossierNaming31168(out string modelFolder);
        string dossierHtml = Path.Combine(repo, "docs", "KOR-DxfToEtabs-dossier.html");
        string dossierPdf = Path.Combine(repo, "docs", "KOR-DxfToEtabs-web.pdf");
        File.WriteAllText(dossierPdf, "%PDF-1.4");
        File.SetLastWriteTime(dossierPdf, DateTime.Now.AddHours(-2));
        File.SetLastWriteTime(dossierHtml, DateTime.Now.AddHours(-1));

        var result = Evaluate(repo, modelFolder);

        Assert.NotNull(result.Refused);
        Assert.Contains("NOT RE-RENDERED", result.Refused!, StringComparison.Ordinal);
        Assert.Contains("KOR-DxfToEtabs-web.pdf", result.Refused!, StringComparison.Ordinal);
    }

    // A half-written or truncated render must be refused by name, not thrown as a PdfPig exception
    // through the publisher. The stub PDFs the other tests use are exactly this shape.
    [Fact]
    public void AnExplainerThatWillNotOpenAsAPdfIsRefusedByName()
    {
        string repo = NewRepoWithDossierNaming31168(out string modelFolder);

        var result = Evaluate(repo, modelFolder);

        Assert.NotNull(result.Refused);
        Assert.Contains("will not open as a PDF", result.Refused!, StringComparison.Ordinal);
        Assert.Contains("KOR-DxfToEtabs-web.pdf", result.Refused!, StringComparison.Ordinal);
    }

    // A side-by-side comparison table flattens to "335 205 Columns 713 304 Floor plates", and a
    // scanner looking for "<number> <noun>" pairs each label with the previous row's OTHER column.
    // This refused a publish whose model matched the dossier on every real number.
    [Fact]
    public void ATwoJobComparisonTableDoesNotInventProseClaims()
    {
        string repo = NewRepoWithDossierNaming31168(out string modelFolder);
        File.WriteAllText(Path.Combine(repo, "docs", "KOR-DxfToEtabs-dossier.html"),
            "<html><body><p>31168 model from drawings</p><table>" +
            "<tr><td>Wall panels</td><td>335</td><td>205</td></tr>" +
            "<tr><td>Columns</td><td>713</td><td>304</td></tr>" +
            "</table></body></html>");

        var refused = Evaluate(repo, modelFolder).Refused ?? string.Empty;

        Assert.DoesNotContain("205 Columns", refused, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no model has that many columns", refused, StringComparison.OrdinalIgnoreCase);
    }

    private static PublishExplainersResult Evaluate(string repo, string modelFolder) =>
        PublishExplainers.Evaluate(new PublishExplainersRequest(
            "31168",
            modelFolder,
            repo,
            NewFolder(),
            Path.Combine(modelFolder, "31168-FROM-DRAWINGS.e2k"),
            Path.Combine(modelFolder, "31168-FROM-DRAWINGS-report.txt"),
            E2kModelContents.Empty,
            SkipDossier: false,
            IsVariant: false));

    private static string NewRepoWithDossierNaming31168(out string modelFolder)
    {
        string repo = NewFolder();
        Directory.CreateDirectory(Path.Combine(repo, "docs"));

        // Prose written first, then rendered -- the order a real build happens in.
        string dossier = Path.Combine(repo, "docs", "KOR-DxfToEtabs-dossier.html");
        File.WriteAllText(dossier, "<html><body>31168 model from drawings</body></html>");
        File.SetLastWriteTime(dossier, DateTime.Now.AddHours(-2));

        foreach (string pdf in new[] { "KOR-DxfToEtabs-web.pdf", "KOR-DxfToEtabs-onepager-web.pdf" })
        {
            string path = Path.Combine(repo, "docs", pdf);
            File.WriteAllText(path, "%PDF-1.4");
            File.SetLastWriteTime(path, DateTime.Now.AddHours(-1));
        }

        Directory.CreateDirectory(Path.Combine(repo, "Kor.Operations.EngineeringTools.Core", "Dxf"));
        modelFolder = NewFolder();
        return repo;
    }

    private static void TouchSource(string repo, string fileName, DateTime when)
    {
        string path = Path.Combine(repo, "Kor.Operations.EngineeringTools.Core", "Dxf", fileName);
        File.WriteAllText(path, "// source");
        File.SetLastWriteTime(path, when);
    }

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "kor-publish-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
