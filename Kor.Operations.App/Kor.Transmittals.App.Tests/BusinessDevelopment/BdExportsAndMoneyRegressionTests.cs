#nullable enable
using System.Globalization;
using System.Reflection;
using DocumentFormat.OpenXml.Packaging;
using Kor.Operations.App.BusinessDevelopment.Briefs;
using Kor.Operations.App.BusinessDevelopment.Reports;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Crm;
using Kor.Operations.App.Opportunities;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Kor.Opportunities.Data.Briefs;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Opportunities;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Brief 7 invariant: amounts, export identities and population labels describe the evidence included.
/// EXPECTED assertions versus ACTUAL production output/state below. Authored only: the brief prohibits
/// builds, tests and database access. No before/after execution or verified status is claimed.
/// Covers migration SQL text, mocked dashboard aggregation, preview identity transitions, exported
/// Deltek text and attack-card filter wiring. Excludes live SQL, Chromium/PDF navigation and JavaScript
/// execution. A browser printing a different DOM after confirmation would escape these gates.
/// </summary>
public sealed class BdExportsAndMoneyRegressionTests
{
    // Before: 331 aliases an unconditional cast as CAD. After: 346 retains original units and
    // guards the CAD projection. Text gate only; SQL syntax/runtime and incorrect stored units escape.
    [Fact]
    public void PipelineMigration_PreservesCurrencyAndGuardsCadProjection()
    {
        var actual = Source("Kor.Opportunities.Data/Schema/346_PrimePipelineCurrency.sql");
        var view = actual[actual.IndexOf("CREATE OR ALTER VIEW", StringComparison.Ordinal)..];
        Assert.Matches(@"(?s)CASE WHEN UPPER\(LTRIM\(RTRIM\(o\.EstimatedValueCurrency\)\)\) = N''CAD''\s+THEN o\.EstimatedValue ELSE NULL END AS decimal\(18,2\)\)\s+AS EstimatedValueCad", view);
        Assert.Contains("CAST(o.EstimatedValue AS decimal(18,2)) AS EstimatedValue", view);
        Assert.Contains("CAST(NULLIF(UPPER(LTRIM(RTRIM(o.EstimatedValueCurrency))), N'''') AS nvarchar(10)) AS EstimatedValueCurrency", view);
        Assert.Matches(@"(?s)CAST\(m.EstimatedCostCad AS decimal\(18,2\)\),\s+CAST\(N''CAD'' AS nvarchar\(10\)\)", view);
        Assert.Contains("o.DismissedAtUtc IS NULL", view);
        Assert.Contains("o.SubmissionDeadlineUtc >= SYSDATETIMEOFFSET()", view);
        Assert.Contains("m.RetiredAtUtc IS NULL", view);
        Assert.Contains("DECLARE @DryRun bit = CASE WHEN $(Apply) = 1 THEN 0 ELSE 1 END", actual);
        Assert.True(actual.IndexOf("IF @DryRun = 1", StringComparison.Ordinal) <
                    actual.IndexOf("EXEC sys.sp_executesql", StringComparison.Ordinal));
        Assert.DoesNotContain(":setvar Apply", actual); // would override the operator's -v Apply=1
        Assert.Contains("EstimatedValue, EstimatedValueCurrency", Source("Kor.Opportunities.Data/MajorProjects/SqlPrimePipelineStore.cs"));
    }

    // Before: USD contributes dollar-for-dollar to CAD headline and bars. After: both withheld,
    // explanatory text visible. The aggregation moved from the dashboard to PipelineCharts on
    // 2026-10-13 (clarity pass) and is pure, so this is the builder itself plus the wiring.
    // Incorrect currencies in the source and unrelated financial panels are not covered.
    [Theory]
    [InlineData("USD")]
    [InlineData(null)]
    [InlineData("")]
    public void PipelineOverview_WithholdsTotalsForNonCadOrUnknownValuedRows(string? currency)
    {
        PrimePipelineRow Row(string? units) => new("Open RFP", "1", "Fixture", null, "Civic", null,
            1_000_000m, "BC", null, null, null)
            { EstimatedValue = 1_000_000m, EstimatedValueCurrency = units };

        var cadOnly = PipelineCharts.Build(new[] { Row("CAD") });
        Assert.Contains("$1.0M", cadOnly.PipelineValueDisplay);
        Assert.Single(cadOnly.MarketValueBars);
        Assert.False(cadOnly.HasUnconvertedValue);

        var mixed = PipelineCharts.Build(new[] { Row("CAD"), Row(currency) });
        Assert.Equal("Unavailable", mixed.PipelineValueDisplay);
        Assert.Empty(mixed.MarketValueBars);
        Assert.Contains("currency", mixed.PipelineValueNote);
        Assert.True(mixed.HasUnconvertedValue);

        Assert.Contains("Overview = PipelineCharts.Build(rows)", Source("Kor.Operations.App/Opportunities/PrimePipelineViewModel.cs"));
        // The dashboard no longer reads the pipeline at all, so it cannot sum it wrongly.
        Assert.DoesNotContain("IPrimePipelineStore", Source("Kor.Operations.App/BusinessDevelopment/Workspace/DashboardViewModel.cs"));
    }

    // Before: generated B enables PDF while A remains displayed. After: pending/failed/stale
    // completion cannot enable it; only successful B carries B's filename. Does not drive WebView2;
    // an incorrectly implemented browser identity probe could escape the behavioral portion.
    [Fact]
    public async Task PdfIdentity_RequiresSuccessfulCurrentRender_AndRejectsLateCompletion()
    {
        var service = new Mock<IBdReportService>();
        service.Setup(x => x.GetTeamingGraphHtmlAsync(It.IsAny<CancellationToken>())).ReturnsAsync("<html><head></head><body>A</body></html>");
        service.Setup(x => x.GetPriorityTreemapHtmlAsync(It.IsAny<CancellationToken>())).ReturnsAsync("<html><head></head><body>B</body></html>");
        var vm = new BdReportsViewModel(service.Object);
        vm.SelectedAnalytical = vm.AnalyticalReports.Single(x => x.Key == "visual-teaming-graph");
        var a = vm.BeginPreviewRender((await vm.BuildAnalyticalPreviewAsync(CancellationToken.None))!)!;
        Assert.False(vm.CanExportPdf);
        vm.CompletePreviewRender(a.Id);
        Assert.True(vm.CanExportPdf);
        Assert.Contains("visual-teaming-graph", vm.RenderedPreview!.PdfFileName);

        vm.SelectedAnalytical = vm.AnalyticalReports.Single(x => x.Key == "visual-priority-treemap");
        var htmlB = (await vm.BuildAnalyticalPreviewAsync(CancellationToken.None))!;
        Assert.False(vm.CanExportPdf);
        var b = vm.BeginPreviewRender(htmlB)!;
        vm.CompletePreviewRender(a.Id);
        Assert.False(vm.CanExportPdf);
        vm.CompletePreviewRender(b.Id, "navigation failed");
        Assert.False(vm.CanExportPdf);
        Assert.Null(vm.RenderedPreview);
        Assert.Contains("navigation failed", vm.StatusMessage);
        vm.CompletePreviewRender(b.Id); // failure is terminal for this attempt
        Assert.False(vm.CanExportPdf);

        var retry = vm.BeginPreviewRender(htmlB)!;
        vm.CompletePreviewRender(retry.Id);
        Assert.True(vm.CanExportPdf);
        Assert.Contains("visual-priority-treemap", vm.RenderedPreview!.PdfFileName);
        var window = Source("Kor.Operations.App/BusinessDevelopment/Reports/BdReportsWindow.xaml.cs");
        Assert.Contains("NavigationCompleted += Preview_NavigationCompleted", window);
        Assert.Contains("document.readyState", window);
        Assert.Contains("kor-preview-id", window);
        Assert.Contains("FileName = rendered.PdfFileName", window);
        Assert.Contains("IsEnabled=\"{Binding CanExportPdf}\"", Source("Kor.Operations.App/BusinessDevelopment/Reports/BdReportsWindow.xaml"));
    }

    // Before: missing AR -> $0 and failed contacts -> 0. After: each unavailable section is
    // explicitly identified in HTML, Word and extracted fallback PDF, with successful zeros distinct.
    // No ODBC/Chromium calls; incorrect source balances or browser PDF layout would escape.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task OrgExports_SeparateUnavailableBalancesAndContactsFromSuccessfulZero(bool arUnavailable, bool contactsUnavailable)
    {
        var service = new Mock<IDeltekClientContextService>();
        var enricher = new OrgBriefDeltekEnricher(service.Object);
        var data = new OrgBriefData(1, "Owner", "Fixture", null, 0, null,
            Array.Empty<OrgRecentProject>(), 0, Array.Empty<OrgRecentProject>(), null, "client", null);
        service.Setup(x => x.LoadAsync("client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltekClientIntelligence { LifetimeFee = 100, Ar = new DeltekArSummary(0, 0, 0) });
        var empty = await enricher.EnrichAsync(data, CancellationToken.None);
        service.Setup(x => x.LoadAsync("client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltekClientIntelligence
            {
                LifetimeFee = 100, ContactsUnavailable = contactsUnavailable, HasDegradedSections = true,
                Ar = arUnavailable ? null : new DeltekArSummary(0, 0, 0)
            });
        var unavailable = await enricher.EnrichAsync(data, CancellationToken.None);
        Assert.Equal(arUnavailable ? (decimal?)null : 0m, unavailable.Deltek!.ArOutstanding);
        Assert.Equal(arUnavailable ? (decimal?)null : 0m, unavailable.Deltek.Ar90Plus);
        Assert.Equal(contactsUnavailable ? (int?)null : 0, unavailable.Deltek.ContactCount);
        foreach (var actual in ExportText(unavailable))
        {
            if (arUnavailable)
            {
                Assert.Contains("could not retrieve AR outstanding", actual);
                Assert.Contains("could not retrieve AR at 90+", actual);
                Assert.DoesNotContain("$0", actual);
            }
            else
            {
                Assert.Contains("$0", actual);
                Assert.DoesNotContain("could not retrieve AR", actual);
            }
            if (contactsUnavailable) Assert.Contains("could not retrieve contacts", actual);
            else Assert.Contains("none on file", actual);
        }
        foreach (var actual in ExportText(empty))
        {
            Assert.Contains("$0", actual);
            Assert.Contains("none on file", actual);
            Assert.DoesNotContain("could not retrieve AR", actual);
            Assert.DoesNotContain("could not retrieve contacts", actual);
        }
        // An unrelated degraded section must not turn a successfully empty contact read unknown.
        service.Setup(x => x.LoadAsync("client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltekClientIntelligence { HasDegradedSections = true });
        Assert.Equal(0, (await enricher.EnrichAsync(data, CancellationToken.None)).Deltek!.ContactCount);
        Assert.Contains("contactsUnavailable = true", Source("Kor.Operations.App/Crm/DeltekClientContextService.cs"));
        Assert.DoesNotContain("if (!anyRow) return null;", Source("Kor.Operations.App/Crm/DeltekClientContextService.cs"));
        Assert.DoesNotContain("intel.Ar?.TotalOutstanding ?? 0m", Source("Kor.Operations.App/Opportunities/OrgDossierWindow.xaml.cs"));
    }

    // Before: render(list) updates cards only. After: same function labels count/title from list,
    // and the input handler supplies its active filter. This is deliberately a generated-JS source
    // gate, NOT a DOM/PDF test. A JS runtime error, pagination or print CSS hiding cards escapes.
    [Fact]
    public void AttackCards_FilterUpdatesPopulationAndTitle_WhileWordKeepsFullSnapshot()
    {
        var actual = BdVisualHtml.Cards("""[{"project":"Alpha"},{"project":"Beta"}]""", 2);
        Assert.Contains("id=\"card-count\"", actual);
        Assert.Contains("id=\"card-title\"", actual);
        Assert.Matches(@"(?s)function render\(list,query\).*?getElementById\('card-count'\)\.textContent=.*?list.length.*?filtered.*?getElementById\('card-title'\)\.textContent=", actual);
        Assert.Contains("document.title=title", actual);
        Assert.Contains(":data,q);", actual);
        var wordHtml = HtmlPreviewBuilder.Render(BdVisualHtml.CardsDocument(actual));
        Assert.Contains("2 live pursuits", wordHtml);
        Assert.Contains("full generated snapshot", wordHtml);
        Assert.Contains("Alpha", wordHtml);
        Assert.Contains("Beta", wordHtml);
    }

    private static string[] ExportText(OrgBriefData data)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        var pdfPath = Path.ChangeExtension(path, ".pdf");
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-CA");
            new BriefGenerator().WriteOrgBrief(data, path);
            using var word = WordprocessingDocument.Open(path, false);
            var html = (string)typeof(HtmlBriefPdfGenerator).GetMethod("BuildOrgHtml", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { data })!;
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            new BriefPdfGenerator().WriteOrgBrief(data, pdfPath);
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            return new[] { html, word.MainDocumentPart!.Document!.Body!.InnerText,
                string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text)) };
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            File.Delete(path);
            File.Delete(pdfPath);
        }
    }

    private static string Source(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, path));
    }
}
