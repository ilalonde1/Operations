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

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "kor-publish-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
