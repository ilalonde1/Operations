#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.BusinessDevelopment.Reports;
using Kor.Opportunities.Data.BdReports;
using Xunit;

namespace Kor.Operations.App.Tests.BdReports;

public sealed class BdReportsViewModelTests
{
    private sealed class StubReportService : IBdReportService
    {
        public Task<IReadOnlyList<SectorVerdictSummary>> GetSectorSummariesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SectorVerdictSummary>>(new[]
            {
                new SectorVerdictSummary("hospitals", "Hospitals & Healthcare", 1, 18, 68, 27, 0, 0, 41, 155),
                new SectorVerdictSummary("schools", "K-12 Schools", 0, 109, 258, 144, 0, 0, 101, 612),
            });

        public Task<IReadOnlyList<PursuitBriefRow>> GetSectorPursuitsAsync(string sectorKey, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PursuitBriefRow>>(new[]
            {
                new PursuitBriefRow(
                    6585, "Plant and Animal Health Centre", "BC", "Industrial", null, "RFP",
                    "Infrastructure BC", 400_000_000m, null, null, null,
                    BdVerdicts.Pursue, "URGENT — teams forming", "RFP stage", "desc", 0.9,
                    DateTimeOffset.Parse("2026-06-08T00:00:00Z"), true),
            });
    }

    [Fact]
    public async Task LoadAsync_PopulatesSectorCards_AndAiContext()
    {
        var vm = new BdReportsViewModel(new StubReportService());

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(2, vm.Sectors.Count);
        Assert.True(vm.HasData);
        Assert.Contains("Hospitals & Healthcare: 19 pursue (1 urgent)", vm.BuildContext());
    }

    [Fact]
    public async Task BuildPreviewAsync_ProducesHtml_AndEnablesDocxExport()
    {
        var vm = new BdReportsViewModel(new StubReportService());
        await vm.LoadAsync(CancellationToken.None);
        vm.SelectedSector = vm.Sectors[0];

        var html = await vm.BuildPreviewAsync(CancellationToken.None);

        Assert.NotNull(html);
        Assert.Contains("Plant and Animal Health Centre", html);
        Assert.Contains("KOR Structural — BC + AB Hospitals BD Report", html);
        Assert.True(vm.CanExportDocx);

        var docx = vm.RenderCurrentDocx();
        Assert.NotEmpty(docx);
    }

    [Fact]
    public void RenderCurrentDocx_BeforePreview_Throws()
    {
        var vm = new BdReportsViewModel(new StubReportService());

        Assert.Throws<InvalidOperationException>(() => vm.RenderCurrentDocx());
    }

    [Fact]
    public async Task BuildPreviewAsync_WithoutSelection_ReturnsNull()
    {
        var vm = new BdReportsViewModel(new StubReportService());
        await vm.LoadAsync(CancellationToken.None);

        Assert.Null(await vm.BuildPreviewAsync(CancellationToken.None));
    }
}
