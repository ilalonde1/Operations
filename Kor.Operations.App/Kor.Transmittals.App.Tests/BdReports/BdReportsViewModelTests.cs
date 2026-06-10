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
                new SectorVerdictSummary("hospitals", "Hospitals & Healthcare", 1, 18, 68, 27, 0, 0, 41, 155, 100, 10, 4, 26_511_000_000m),
                new SectorVerdictSummary("schools", "K-12 Schools", 0, 109, 258, 144, 0, 0, 101, 612, 500, 11, 0, 19_869_000_000m),
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

        public Task<IReadOnlyList<PursuitBriefRow>> GetCallSheetPoolAsync(CancellationToken ct)
            => GetSectorPursuitsAsync("hospitals", ct);

        public Task<BdExecHeadline> GetExecHeadlineAsync(CancellationToken ct)
            => Task.FromResult(new BdExecHeadline(1942, 1425, 93_822_000_000m));

        public Task<IReadOnlyList<ArchitectLeverageRow>> GetArchitectLeverageAsync(int take, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ArchitectLeverageRow>>(new[]
            {
                new ArchitectLeverageRow(7, "Chris Dikeakos Architects Inc.", 21, 887_000_000m, 12, 4, 14,
                    new[] { "100: Tower One (BC) — $400M" }),
                new ArchitectLeverageRow(99, "Unknown Firm", 3, 10_000_000m, 0, 1, 0, Array.Empty<string>()),
            });

        public Task<IReadOnlyList<CompetitorFootprintRow>> GetCompetitorFootprintAsync(int take, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CompetitorFootprintRow>>(new[]
            {
                new CompetitorFootprintRow(1, "Aspect Structural Engineers", 37, 6, 18, 2,
                    new[] { "10: Sample Tower (BC) — $200M" }),
            });

        public Task<IReadOnlyList<string>> GetProjectsMentioningAsync(string orgPattern, int take, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "6585: Plant and Animal Health Centre (BC) — $400M" });

        public List<(string Category, string Format)> Logged { get; } = new();

        public Task LogReportGeneratedAsync(string category, string format, string generatedByUser, int? recordCount, string? notes, CancellationToken ct)
        {
            lock (Logged)
            {
                Logged.Add((category, format));
            }

            return Task.CompletedTask;
        }
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
