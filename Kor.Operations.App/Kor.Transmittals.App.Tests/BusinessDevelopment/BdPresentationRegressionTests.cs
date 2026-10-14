#nullable enable
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using Kor.Operations.App.BusinessDevelopment;
using Kor.Operations.App.BusinessDevelopment.Briefs;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.BusinessDevelopment.Reports;
using Kor.Operations.App.Opportunities;
using Kor.Opportunities.Data.BdReports.Generators;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.Briefs;
using Kor.Opportunities.Core.Models;
using Xunit;
using Moq;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// EXPECTED semantic labels/population/date/party versus ACTUAL export content and consumer output.
/// Authored only under Brief 4's no-execution constraint. Individual limits are below.
/// </summary>
public sealed class BdPresentationRegressionTests
{
    // The billed-source notice is MEASURED from the posting period, never asserted: the
    // first cut hard-coded "frozen since 2026-02-17" and shipped a week after the refresh
    // had been re-run (Ian, 2026-10-13: "We unfroze that last week"). Covers stale,
    // current and unknown periods. Does not touch Deltek; a wrong MAX(Period) escapes.
    [Fact]
    public void BilledSourceNotice_IsMeasuredFromThePostingPeriod_NotAsserted()
    {
        OrgBriefDeltekSection Section(DateTime? postedThrough) =>
            new OrgBriefDeltekSection("C1", "Client", 1, 100000m, null, null,
                Array.Empty<OrgBriefDeltekProject>(), 1, 5000m, 1000m, false) { BilledPostedThrough = postedThrough };
        var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        var stale = Section(thisMonth.AddMonths(-8));
        Assert.True(stale.BilledSourceIsStale);
        Assert.True(stale.DegradedSections);
        Assert.Contains("8 months behind", stale.BilledSourceNotice);
        Assert.Contains(thisMonth.AddMonths(-8).ToString("MMM yyyy"), stale.BilledSourceNotice);

        var current = Section(thisMonth);
        Assert.False(current.BilledSourceIsStale);
        Assert.False(current.DegradedSections);
        Assert.DoesNotContain("behind", current.BilledSourceNotice);
        Assert.Contains("posted through " + thisMonth.ToString("MMM yyyy"), current.BilledSourceNotice);

        var closeLag = Section(thisMonth.AddMonths(-2));   // the financials module's normal close lag
        Assert.False(closeLag.BilledSourceIsStale);

        var unknown = Section(null);
        Assert.False(unknown.BilledSourceIsStale);
        Assert.Contains("could not be read", unknown.BilledSourceNotice);
        Assert.DoesNotContain("frozen since", stale.BilledSourceNotice + current.BilledSourceNotice + unknown.BilledSourceNotice);
    }

    // Covers DOCX, production HTML content, extracted fallback PDF text, and a
    // successful-but-stale billing section. Excludes browser PDF and live
    // balances; browser printing that hides the disclosure could escape this gate.
    [Fact]
    public void FeesAreNotBilled_AndOnlyBilledSourceIsFrozen()
    {
        var section = new OrgBriefDeltekSection("C1", "Client", 1, 100000m, null, null,
            new[] { new OrgBriefDeltekProject("P1", "Project", null, null, 100000m, 20000m) },
            1, 5000m, 1000m, false)
            { BilledPostedThrough = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-8) };
        Assert.True(section.DegradedSections);
        Assert.False(section.UnavailableSections);
        var data = new OrgBriefData(1, "Owner", "Client", null, 0, null,
            Array.Empty<OrgRecentProject>(), 0, Array.Empty<OrgRecentProject>(), null, "C1", section);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        var pdfPath = Path.ChangeExtension(path, ".pdf");
        try
        {
            new BriefGenerator().WriteOrgBrief(data, path);
            using var word = WordprocessingDocument.Open(path, false);
            var html = (string)typeof(HtmlBriefPdfGenerator).GetMethod("BuildOrgHtml",
                BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { data })!;
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            new BriefPdfGenerator().WriteOrgBrief(data, pdfPath);
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var pdfText = string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
            foreach (var actual in new[] { word.MainDocumentPart!.Document!.Body!.InnerText, html, pdfText })
            {
                Assert.Contains("Lifetime fee", actual);
                Assert.DoesNotContain("Lifetime billed", actual);
                Assert.Contains(section.BilledSourceNotice, actual);
                Assert.Contains("8 months behind", actual);
                Assert.DoesNotContain("frozen since", actual);
                Assert.Contains("Fee and AR use different sources", actual);
                Assert.Contains("AR outstanding", actual);
                Assert.DoesNotContain("Some Deltek sections were unavailable", actual);
                Assert.Contains(100000m.ToString("C0", CultureInfo.CurrentCulture), actual);
                Assert.Contains(20000m.ToString("C0", CultureInfo.CurrentCulture), actual);
            }
            var fallback = Source("BusinessDevelopment/Briefs/BriefPdfGenerator.cs");
            Assert.Contains("DeltekFactRow(table, \"Lifetime fee\"", fallback);
            Assert.DoesNotContain("Lifetime billed", fallback);
            Assert.Contains("Text(dk.BilledSourceNotice)", fallback);
            Assert.Contains("if (dk.UnavailableSections)", fallback);
        }
        finally { File.Delete(path); File.Delete(pdfPath); }
    }

    // Covers every card in the preview's serialized snapshot through final DOCX,
    // including the last of 270, plus window dispatch. No browser/PDF/layout gate;
    // omitted fields in BOTH preview and Word can escape.
    [Fact]
    public async Task CardsDocxContainsWholeSnapshot()
    {
        var json = JsonSerializer.Serialize(Enumerable.Range(1, 270).Select(i => new
        {
            id = i, project = $"Project-{i:D3}", province = "BC", verdict = "PURSUE",
            cost = (decimal?)null, arch = "Architect", archPeople = 1,
            se = "KOR Structural Ltd.", seIsKor = true, gc = "GC", owner = "Owner"
        }));
        var html = BdVisualHtml.Cards(json, 270);
        var service = new Mock<IBdReportService>();
        service.Setup(s => s.GetAttackCardsHtmlAsync(It.IsAny<CancellationToken>())).ReturnsAsync(html);
        var vm = new BdReportsViewModel(service.Object)
        {
            SelectedAnalytical = new AnalyticalReportVm("visual-attack-cards", "Opportunity Attack Cards", "")
        };
        await vm.BuildAnalyticalPreviewAsync(CancellationToken.None);
        Assert.True(vm.CurrentIsVisual);
        Assert.False(vm.CurrentIsVisualSnapshot);
        using var stream = new MemoryStream(vm.RenderCurrentDocx());
        using var word = WordprocessingDocument.Open(stream, false);
        var actual = word.MainDocumentPart!.Document!.Body!.InnerText;
        foreach (var i in Enumerable.Range(1, 270)) Assert.Contains($"Project-{i:D3}", actual);
        Assert.Contains("270 live pursuits", actual);
        Assert.Contains("recorded KOR", actual);
        Assert.Contains("CardsDocument(html)", Source("BusinessDevelopment/Reports/BdReportsViewModel.cs"));
        Assert.Contains("CurrentIsVisualSnapshot", Source("BusinessDevelopment/Reports/BdReportsWindow.xaml.cs"));
    }

    // Both brief formatters AND Dashboard row factory across spring/fall DST,
    // UTC-midnight discrepancy and non-hour offsets; instant-based countdown.
    // 2026-10-14: the shared format became the reader's LOCAL wall clock in words
    // ("Mon Oct 19, 2:00 PM"); the stored offset moved to Stored() for the editor and
    // tooltips. Every consumer must still agree with the one formatter.
    // Excludes timestamp ingestion/storage and screenshots: clipped UI text escapes.
    [Theory]
    [InlineData("2026-03-08T01:30:00-08:00")]
    [InlineData("2026-03-08T03:30:00-07:00")]
    [InlineData("2026-11-01T01:30:00-07:00")]
    [InlineData("2026-11-01T01:30:00-08:00")]
    [InlineData("2031-01-01T00:30:00+00:00")]
    [InlineData("2026-03-08T00:30:00+05:30")]
    public void DeadlinesReadAsLocalTimeInEveryConsumer(string input)
    {
        var deadline = DateTimeOffset.Parse(input, CultureInfo.InvariantCulture);
        var now = deadline.AddHours(-36);
        var local = deadline.ToLocalTime();
        var expectedDate = local.ToString(
            local.Year == DateTimeOffset.Now.Year ? "ddd MMM d, h:mm tt" : "ddd MMM d yyyy, h:mm tt",
            CultureInfo.InvariantCulture);
        Assert.DoesNotContain("stored offset", expectedDate);
        Assert.DoesNotContain("UTC", expectedDate);
        var row = DashboardViewModel.CreateDeadlineRow(new Opportunity { SubmissionDeadlineUtc = deadline, BuyerName = "Fraser Health" }, now);
        Assert.Equal(expectedDate, row.Deadline);
        Assert.Equal("Fraser Health · " + expectedDate, row.Detail);   // null province is skipped, not rendered as a gap
        Assert.Equal(deadline.ToString("yyyy-MM-dd HH:mm 'UTC'zzz", CultureInfo.InvariantCulture),
            OpportunityDeadlineDisplay.Stored(deadline));                 // the editor still sees the stored value
        var opportunity = new Opportunity { SubmissionDeadlineUtc = deadline };
        Assert.Equal(expectedDate, new OpportunityRowView(opportunity).DeadlineDisplay);
        Assert.Equal(expectedDate, new OpportunityDeadlineConverter().Convert(deadline, typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("1 day 12 hr remaining", row.Countdown);
        Assert.Equal(expectedDate + " — " + row.Countdown, BriefGenerator.FormatDeadline(deadline, now));
        Assert.Equal(BriefGenerator.FormatDeadline(deadline, now), BriefPdfGenerator.FormatDeadline(deadline, now));
        Assert.Contains("BriefPdfGenerator.FormatDeadline(d.SubmissionDeadlineUtc)",
            Source("BusinessDevelopment/Briefs/HtmlBriefPdfGenerator.cs"));
        Assert.Contains(".Select(o => CreateDeadlineRow(o, now))", Source("BusinessDevelopment/Workspace/DashboardViewModel.cs"));
        Assert.DoesNotContain("LocalDateTime", Source("BusinessDevelopment/Workspace/DashboardViewModel.cs"));
        Assert.Equal("expired", OpportunityDeadlineDisplay.Countdown(deadline, deadline.AddMinutes(1)));
        Assert.Equal("less than 1 min remaining", OpportunityDeadlineDisplay.Countdown(deadline, deadline.AddSeconds(-1)));
        Assert.Equal("due now", OpportunityDeadlineDisplay.Countdown(deadline, deadline));
        Assert.Equal("not specified", BriefGenerator.FormatDeadline(null, now));
        Assert.Equal("not specified", BriefPdfGenerator.FormatDeadline(null, now));
    }

    // Display resources must use the same policy too. Does not instantiate WPF;
    // a resource lookup failure or narrow container could still escape.
    [Fact]
    public void DeadlineBindingsUseSharedDisplay()
    {
        foreach (var file in new[] { "CompetitionInfoView.xaml", "CompetitionInfoWindow.xaml", "HistoricalOpportunityDetailWindow.xaml" })
        {
            var actual = Source("Opportunities/" + file);
            Assert.Contains("SubmissionDeadlineUtc, Converter={StaticResource OpportunityDeadlineConverter}", actual);
            Assert.Contains("<local:OpportunityDeadlineConverter", actual);
        }
        Assert.Contains("OpportunityDeadlineDisplay.Stored(_existing.SubmissionDeadlineUtc)", Source("Opportunities/OpportunityEntryDialog.xaml.cs"));
        Assert.Contains("Edit date (preserves stored time and offset; new deadlines use local midnight)", Source("Opportunities/OpportunityEntryDialog.xaml"));
        Assert.Contains("Binding Countdown", Source("BusinessDevelopment/Workspace/DashboardView.xaml"));
    }

    /// <summary>
    /// EXPECTED invariant local dates versus ACTUAL shared formatter output, including UTC,
    /// local and unspecified DateTime inputs. Does not render; clipped date labels escape.
    /// </summary>
    [Fact]
    public void BdDateDisplay_RendersOneFormat()
    {
        var instant = DateTimeOffset.Parse("2026-09-09T19:17:00Z", CultureInfo.InvariantCulture);
        var local = instant.ToLocalTime();
        var expected = local.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        Assert.Equal(expected, BdDateDisplay.Day(instant));
        Assert.Equal(expected, BdDateDisplay.Day(instant.UtcDateTime));
        Assert.Equal(expected, BdDateDisplay.Day(instant.LocalDateTime));
        Assert.Equal(expected, BdDateDisplay.Day(DateTime.SpecifyKind(instant.LocalDateTime, DateTimeKind.Unspecified)));
        Assert.Equal(local.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture), BdDateDisplay.DayAndTime(instant));
    }

    /// <summary>
    /// Literals only in the six harnessed files; nothing is rendered. Culture formats
    /// such as StringFormat={}{0:d} or ToString("d") would escape this source scan.
    /// </summary>
    [Fact]
    public void NonDeadlineDates_OnHarnessedScreens_GoThroughBdDateDisplay()
    {
        foreach (var file in new[]
        {
            "Crm/CrmEngagementRowView.cs", "Crm/CrmActivityRowView.cs", "Crm/CrmViewModel.cs",
            "BusinessDevelopment/Workspace/OverwatchRowView.cs", "Opportunities/OpportunityDetailViewModel.cs",
            "BusinessDevelopment/Workspace/DashboardView.xaml",
        })
        {
            var source = Source(file);
            foreach (var format in new[] { "\"yyyy-MM-dd", ":yyyy-MM-dd", "\"MMM d, yyyy" })
                Assert.False(source.Contains(format, StringComparison.Ordinal),
                    $"EXPECTED shared date display in {file}; ACTUAL inline {format}");
        }
        Assert.Contains("RefreshedAtUtc, Converter={StaticResource BdDay}, StringFormat=as of {0}",
            Source("BusinessDevelopment/Workspace/DashboardView.xaml"));
    }

    /// <summary>
    /// Counts literal formats, not rendered dates: the other 64 occurrences in 23 files
    /// are counted, not fixed (source inventory, 2026-10-14; brief reported 63).
    /// Culture formats such as StringFormat={}{0:d} or ToString("d") would escape.
    /// </summary>
    [Fact]
    public void InlineDateFormats_OnBdScreens_OnlyGoDown()
    {
        var app = Path.Combine(RepoRoot(), "Kor.Operations.App");
        var counts = new List<(string File, int Count)>();
        foreach (var folder in new[] { "Crm", "BusinessDevelopment/Workspace", "Opportunities" })
        foreach (var pattern in new[] { "*.cs", "*.xaml" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(app, folder), pattern, SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) is "BdDateDisplay.cs" or "OpportunityDeadlineDisplay.cs") continue;
            var count = Regex.Matches(File.ReadAllText(file), @"yyyy-MM-dd|MMM d, yyyy").Count;
            if (count > 0) counts.Add((Path.GetRelativePath(app, file), count));
        }
        var total = counts.Sum(c => c.Count);
        Assert.True(total <= 64, $"EXPECTED <= 64 inline date formats; ACTUAL {total}:\n"
            + string.Join("\n", counts.OrderBy(c => c.File).Select(c => $"{c.File}: {c.Count}")));
    }

    // Resolved KOR, other structural firm, unresolved name. Covers shared prose,
    // DOCX/HTML/fallback PDF artifacts for KOR, and wiring to all brief paths.
    // Excludes browser printing and identity resolution: wrong upstream Kind escapes.
    [Fact]
    public void StructuralSeatDoesNotMakeKorItsOwnCompetitor()
    {
        var kor = new LinkedOrgSummary(1, "KOR Structural Ltd.", "KorStructural", 0, 0, 0, null);
        Assert.Equal("KOR's own recorded structural seat on this pursuit.",
            BriefPdfGenerator.StructuralSeatCallout("KOR", kor));
        Assert.Contains("competitor", BriefPdfGenerator.StructuralSeatCallout("Other", kor with { Kind = "Structural", DisplayName = "Other" }));
        Assert.DoesNotContain("competitor", BriefPdfGenerator.StructuralSeatCallout("KOR Structural Ltd.", null));
        Assert.DoesNotContain("vs KOR", BriefPdfGenerator.StructuralSeatCallout("Other", null));
        Assert.Contains("BriefPdfGenerator.StructuralSeatCallout(sourceName, summary)", Source("BusinessDevelopment/Briefs/BriefGenerator.cs"));
        Assert.Contains("StructuralSeatCallout(sourceName, summary)", Source("BusinessDevelopment/Briefs/BriefPdfGenerator.cs"));
        Assert.Contains("BriefPdfGenerator.ProjectTeamBullets", Source("BusinessDevelopment/Briefs/HtmlBriefPdfGenerator.cs"));

        var project = new ProjectBriefData(
            Id: 2237, ProjectName: "5740 Cambie Condominiums", Province: "BC", City: null,
            Region: null, Sector: null, SubSector: null, Stage: null, EstimatedCostCad: null,
            EstimatedCostText: null, StartYear: null, CompletionYear: null, ScheduleNotes: null,
            ProponentName: null, ArchitectName: null, StructuralEngineerName: kor.DisplayName,
            GeneralContractorName: null, SourceUrl: null, ProjectDescription: null,
            ProponentSummary: null, ArchitectSummary: null, StructuralSummary: kor,
            GeneralContractorSummary: null, MentionsInWorkHistory: Array.Empty<ProjectMentionInWorkHistory>(),
            RecentMentionSignals: Array.Empty<ProjectRecentMentionSignal>(),
            OpenActionsMentioning: Array.Empty<ProjectOpenActionMention>(), IntelDescription: null,
            IntelSchedule: null, IntelStatus: null, IntelKorAngle: null,
            IntelKeyPeople: Array.Empty<ProjectIntelKeyPersonRow>(), IntelSignals: Array.Empty<ProjectIntelSignalRow>(),
            IntelActions: Array.Empty<ProjectIntelActionRow>(), IntelRisks: Array.Empty<ProjectIntelRiskRow>());
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        var pdfPath = Path.ChangeExtension(path, ".pdf");
        try
        {
            new BriefGenerator().WriteProjectBrief(project, path);
            using var word = WordprocessingDocument.Open(path, false);
            var html = (string)typeof(HtmlBriefPdfGenerator).GetMethod("BuildProjectHtml",
                BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { project })!;
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            new BriefPdfGenerator().WriteProjectBrief(project, pdfPath);
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var pdfText = string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
            foreach (var actual in new[] { word.MainDocumentPart!.Document!.Body!.InnerText, System.Net.WebUtility.HtmlDecode(html), pdfText })
            {
                Assert.Contains("KOR's own recorded structural seat", actual);
                Assert.DoesNotContain("KOR's competitor", actual);
                Assert.DoesNotContain("vs KOR", actual);
            }
        }
        finally { File.Delete(path); File.Delete(pdfPath); }
    }

    private static string Source(string path)
        => File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.App", path));

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
