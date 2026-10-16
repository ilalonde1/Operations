#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Kor.Operations.App.Brochures;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.FeeProposal;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using Kor.Operations.Rendering.Proposal;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Kor.Opportunities.Data.Opportunities;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Covers document text/metadata, deterministic draft checks, manual-write exclusion, generic items,
/// org-chart labels/PNG, personnel prefill, rate math/flags/Word bolding/template roundtrip,
/// read-only guidance and awaited brochure attachment success/failure/unsubscription.
/// Does not execute SQL, exercise actual builder dialogs, inspect visual quality or prove deployment.
/// Same-class fault missed: a real Word producer could encode an unsupported alternative textbox shape.
/// </summary>
public sealed class BdDirectorBuildersRegressionTests
{
    [Fact]
    public void Docx_ParagraphCellAndAlternateTextbox_AreReadOnce_WithMetadata()
    {
        using var stream = new MemoryStream(DocxFixture());
        var draft = ProposalDocxReader.Read(stream);
        Assert.Equal(new[] { "Body paragraph", "Table cell", "One textbox" }, draft.Text.Split('\n'));
        Assert.Equal("Author", draft.Metadata.Author);
        Assert.Equal("Reviewer", draft.Metadata.LastModifiedBy);
        Assert.Equal("7", draft.Metadata.Revision);
        Assert.Equal(2026, draft.Metadata.LastSaved!.Value.Year);
        // Identical wording in distinct boxes is not a fallback and must not be removed.
        using var separate = new MemoryStream(DocxFixture(distinctBox: true));
        Assert.Equal(2, ProposalDocxReader.Read(separate).Text.Split('\n').Count(p => p == "One textbox"));
    }

    [Fact]
    public async Task DraftChecks_UseActualPdfPages_PhrasesAndAnchoredFilenamePattern_WithNotes()
    {
        const string name = "2026-RFP-IP-061 - KOR Structural - Proposal.pdf";
        using var pdf = new MemoryStream(PdfFixture());
        var draft = await DraftDocument.ReadAsync(name, pdf, CancellationToken.None);
        Assert.Equal(3, draft.Pages);
        foreach (var (kind, parameter, expected) in new[]
        {
            ("PhrasePresent", "BODY PARAGRAPH", "Met"), ("PhrasePresent", "absent wording", "NotMet"),
            ("PageLimit", "2", "NotMet"), ("PageLimit", "3", "Met"),
            ("FileName", "{rfp} - KOR Structural - *.pdf", "Met"), ("FileName", "{rfp} - Resumes.pdf", "NotMet")
        })
        {
            var result = PursuitDraftChecker.Check(Requirement(1, kind, parameter), draft, "2026-RFP-IP-061");
            Assert.NotNull(result);
            Assert.Equal(expected, result.Status);
            Assert.Contains(name, result.Note);
        }
        Assert.Equal("NotMet", PursuitDraftChecker.Check(Requirement(1, "FileName", "{rfp} - KOR Structural - *.pdf"),
            draft with { FileName = name + ".exe" }, "2026-RFP-IP-061")!.Status);
        Assert.Equal("Met", PursuitDraftChecker.Check(Requirement(1, "FileName", "{rfp} - *.pdf"),
            draft with { FileName = "RFP[1]+ - Proposal.pdf" }, "RFP[1]+")!.Status);
        Assert.Contains("3 PDF pages", PursuitDraftChecker.Check(Requirement(1, "PageLimit", "2"), draft, "rfp")!.Note);
        using var word = new MemoryStream(DocxFixture());
        var docx = await DraftDocument.ReadAsync("draft.docx", word, CancellationToken.None);
        var open = PursuitDraftChecker.Check(Requirement(1, "PageLimit", "2", "Met"), docx, "rfp")!;
        Assert.Equal("Open", open.Status);
        Assert.Contains("Render to PDF to count pages", open.Note);
        Assert.Equal("Met", PursuitDraftChecker.Check(Requirement(1, "PhrasePresent", "One textbox"), docx, "rfp")!.Status);
    }

    [Fact]
    public async Task CheckDraft_NeverWritesManualItems_AndAttributesAutomaticResults()
    {
        var items = new List<PursuitRequirement>
        {
            Requirement(1, "Manual", null, "NotApplicable") with { StatusNote = "Person's reason", CheckedBy = "original" },
            Requirement(2, "PhrasePresent", "One textbox"), Requirement(3, "PageLimit", "2", "Met")
        };
        var original = items[0];
        var store = new Mock<IPursuitRequirementStore>(MockBehavior.Strict);
        store.Setup(s => s.ListAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(() => items.ToArray());
        store.Setup(s => s.UpdateStatusAsync(1, It.Is<long>(id => id != 1), It.IsAny<string>(), It.IsAny<string>(), "reviewer", true, It.IsAny<CancellationToken>()))
            .Callback<long, long, string, string?, string, bool, CancellationToken>((_, id, status, note, by, _, _) =>
            {
                var index = items.FindIndex(i => i.Id == id);
                items[index] = items[index] with { Status = status, StatusNote = note, CheckedBy = by };
            }).Returns(Task.CompletedTask);
        var files = new Mock<IPursuitFileStore>(MockBehavior.Strict);
        files.Setup(s => s.OpenContentAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream(DocxFixture()));
        var vm = new PursuitChecklistViewModel(store.Object, files.Object, 1, "rfp", "reviewer")
        { SelectedAttachment = new ChecklistAttachmentRow(Attachment("draft.docx")) };
        await vm.CheckDraftAsync();
        Assert.Equal(original, items[0]);
        Assert.Equal("Met", items[1].Status);
        Assert.Equal("reviewer", items[1].CheckedBy);
        Assert.Equal("Open", items[2].Status);
        Assert.Contains("Render to PDF", items[2].StatusNote);
        store.Verify(s => s.UpdateStatusAsync(1, 1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.UpdateStatusAsync(1, It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), "reviewer", true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GenericChecklist_InsertsEightHandoffItems_AsManualWithSourceAndReminder()
    {
        var store = new Mock<IPursuitRequirementStore>(MockBehavior.Strict);
        IReadOnlyList<PursuitRequirementInput>? written = null;
        store.Setup(s => s.AddAsync(1, It.IsAny<IReadOnlyList<PursuitRequirementInput>>(), "person", GenericPursuitChecklist.Note, It.IsAny<CancellationToken>()))
            .Callback<long, IReadOnlyList<PursuitRequirementInput>, string, string?, CancellationToken>((_, items, _, _, _) => written = items)
            .Returns(Task.CompletedTask);
        store.Setup(s => s.ListAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<PursuitRequirement>());
        await new PursuitChecklistViewModel(store.Object, Mock.Of<IPursuitFileStore>(), 1, "rfp", "person").AddGenericAsync();
        Assert.NotNull(written);
        Assert.Equal(8, written.Count);
        Assert.All(written, item => { Assert.Equal("Manual", item.CheckKind); Assert.Equal("Generic (Leduc 2026-RFP-IP-061)", item.SourceRef); });
        Assert.Equal("verify against this RFP", GenericPursuitChecklist.Note);
        var joined = string.Join("\n", written.Select(i => i.Requirement));
        foreach (var text in new[] { "Cover:", "Sections in the RFP's order", "Page limit", "signature", "AI disclosure", "incorporates AI",
            "succession for SMT and PMT", "$65M", "IPD mechanics", "References ≥ 3", "named contact with title", "scope, date, duration", "Rates for everyone", "Addenda" })
            Assert.Contains(text, joined);
        PursuitRequirementValidation.Validate(written);
    }

    [Theory]
    [InlineData("", "RFP 1", "Manual", null)]
    [InlineData("Quote", "", "Manual", null)]
    [InlineData("Quote", "RFP 1", "Invented", null)]
    [InlineData("Quote", "RFP 1", "PhrasePresent", " ")]
    [InlineData("Quote", "RFP 1", "FileName", null)]
    [InlineData("Quote", "RFP 1", "PageLimit", "-1")]
    [InlineData("Quote", "RFP 1", "PageLimit", "2.5")]
    public void InvalidRequirementsAreRejected(string quote, string source, string kind, string? parameter)
        => Assert.Throws<ArgumentException>(() => PursuitRequirementValidation.Validate(new[] { new PursuitRequirementInput(quote, source, kind, parameter) }));

    [Fact]
    public void OrgChart_LeducExampleHasEachPersonLabelOnce_UppercaseRoles_AndNonemptyPng()
    {
        OnSta(() =>
        {
            var spec = OrgChartSpec.FromJson(File.ReadAllText(Path.Combine(RepoRoot(), "tools/New-ProposalOrgChart.example.json")));
            var rendered = OrgChartRenderer.Render(spec);
            var tree = XDocument.Parse(rendered.Svg);
            XNamespace svg = "http://www.w3.org/2000/svg";
            var labels = tree.Descendants(svg + "text").Where(e => (string?)e.Attribute("data-kind") == "person-name").Select(e => e.Value).ToArray();
            var people = spec.Chain.Concat(spec.Groups.SelectMany(g => g.People)).ToArray();
            Assert.Equal(people.Length, labels.Length);
            foreach (var person in people) Assert.Single(labels, text => text == person.Name);
            Assert.Contains("John Markulin", labels); Assert.Contains("Jim DesRoches", labels);
            Assert.Contains("ALTERNATE PMT", rendered.Svg);
            Assert.NotEmpty(tree.Descendants(svg + "line"));
            Assert.True(rendered.Png.Length > 1000);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, rendered.Png.Take(8));
            using var json = JsonDocument.Parse(spec.ToJson());
            Assert.Equal(new[] { "title", "subtitle", "context", "chain", "groups", "note" }, json.RootElement.EnumerateObject().Select(p => p.Name));
            Assert.Equal(spec.Chain[0].Name, OrgChartSpec.FromJson(spec.ToJson()).Chain[0].Name);
        });
    }

    [Fact]
    public void OrgChart_PrefillsPersonnelNamesCredentialsTitles_WithoutInventingRfpRoles()
    {
        var personnel = new PersonnelBlockContent { LeadStaffId = "a", SupportingStaffId = "b", CollaborationNote = "Continuity note" };
        personnel.AdditionalStaffIds.Add("c"); personnel.AdditionalStaffIds.Add("a");
        var staff = new[] { "a", "b", "c" }.Select(id => new ProposalStaffMember { Id = id, FullName = "Name " + id, Credentials = "P.Eng.", Title = "Engineer" }).ToArray();
        var spec = PursuitOrgChartViewModel.FromPersonnel(personnel, staff, "Project", "RFP");
        Assert.Equal(new[] { "Name a", "Name b" }, spec.Chain.Select(p => p.Name));
        Assert.Equal("Name c", Assert.Single(Assert.Single(spec.Groups).People).Name);
        Assert.All(spec.Chain, p => { Assert.Equal("P.Eng.", p.Cred); Assert.Equal("Engineer", p.Title); Assert.Empty(p.Role); });
        Assert.Equal("Continuity note", spec.Note);
    }

    [Fact]
    public async Task RateSchedule_ComputesFlags_SurvivesLibraryRoundtrip_AndRendersScoredWordRowsBold()
    {
        var content = new RfpRateScheduleBlockContent { Burden = .30m, Overhead = .24m, Profit = .10m, StatutoryLoad = .12m };
        var scored = new RfpRateScheduleRow { Name = "Scored Engineer", Position = "Design Lead", Scored = true, BaseHourly = 50m };
        content.Rows.Add(scored);
        var result = content.Calculate(scored);
        Assert.Equal(65m, result.ExcludingOverhead); Assert.Equal(80.60m, result.IncludingOverhead); Assert.Equal(88.66m, result.IncludingOverheadAndProfit);
        Assert.Empty(content.Flags);
        scored.QuotedHourly = 104m; // 104 / 65 - 1 = 60% overhead.
        Assert.Equal(.60m, content.Calculate(scored).ImpliedOverhead);
        Assert.Contains(content.Flags, f => f.Contains("quoted hourly implies", StringComparison.Ordinal));
        scored.QuotedHourly = 80.925m; // exactly 0.5 percentage points is not a flag.
        Assert.Empty(content.Flags);
        content.Burden = .05m;
        Assert.Contains(content.Flags, f => f.Contains("below the stated statutory load", StringComparison.Ordinal));
        content.Rows.Add(new RfpRateScheduleRow { Name = "Missing", Position = "BIM Lead", Scored = true });
        Assert.Contains(content.Flags, f => f.Contains("scored position has no rate", StringComparison.Ordinal));
        content.Rows.RemoveAt(1); content.Burden = .30m; scored.QuotedHourly = null;
        content.Rows.Add(new RfpRateScheduleRow { Name = "Other Engineer", Position = "Support", BaseHourly = 40 });
        var library = new Mock<IProposalBlockLibraryStore>();
        ProposalBlockTemplate? saved = null;
        library.Setup(s => s.SaveAsync(It.IsAny<ProposalBlockTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<ProposalBlockTemplate, CancellationToken>((t, _) => saved = JsonSerializer.Deserialize<ProposalBlockTemplate>(JsonSerializer.Serialize(t)))
            .Returns(Task.CompletedTask);
        library.Setup(s => s.LoadAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProposalBlockTemplate>());
        var vm = new FeeProposalBuilderViewModel(Mock.Of<IFeeProposalStore>(), library.Object, Mock.Of<IProposalStaffStore>());
        var block = new FeeProposalBlock { BlockType = ProposalBlockType.RfpRateSchedule, RfpRateSchedule = content };
        await vm.SaveAsTemplateAsync(new FeeProposalBlockViewModel(block), "RFP template");
        Assert.NotNull(saved);
        vm.InsertFromTemplate(saved);
        Assert.Equal(88.66m, Assert.Single(vm.Blocks).Block.RfpRateSchedule!.Calculations[0].IncludingOverheadAndProfit);
        vm.OpenProposal(new Kor.Operations.Core.Models.Proposal.FeeProposal { Blocks = new() { block } });
        Assert.False(vm.IsDirty);
        content.Rows[1].BaseHourly = 41;
        Assert.True(vm.IsDirty);
        var path = Path.Combine(Path.GetTempPath(), "kor-rate-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            await new FeeProposalDocxRenderer().RenderAsync(new Kor.Operations.Core.Models.Proposal.FeeProposal { Blocks = new() { block } }, Array.Empty<ProposalStaffMember>(), path, CancellationToken.None);
            using var docx = WordprocessingDocument.Open(path, false);
            var body = docx.MainDocumentPart!.Document!.Body!;
            Assert.Contains("24%", body.InnerText); Assert.Contains("10%", body.InnerText);
            Assert.Contains("65.00", body.InnerText); Assert.Contains("80.60", body.InnerText); Assert.Contains("88.66", body.InnerText);
            var scoredRow = body.Descendants<TableRow>().Single(r => r.InnerText.Contains("Scored Engineer", StringComparison.Ordinal));
            Assert.All(scoredRow.Descendants<Run>().Where(r => r.Descendants<Text>().Any()), run => Assert.NotNull(run.RunProperties?.GetFirstChild<Bold>()));
            var ordinary = body.Descendants<TableRow>().Single(r => r.InnerText.Contains("Other Engineer", StringComparison.Ordinal));
            Assert.All(ordinary.Descendants<Run>(), run => Assert.Null(run.RunProperties?.GetFirstChild<Bold>()));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Guidance_EveryBlockReadsLiveShipleyRulesByTitle_WithCitationsAndNoWrites()
    {
        var store = new Mock<IKnowledgeClaimStore>(MockBehavior.Strict);
        store.Setup(s => s.ListSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        { new KnowledgeSourceSummary(87, ProposalGuidance.SourceTitle, "Shipley", 10, 10, 12, 0, 1) });
        store.Setup(s => s.ListClaimsAsync(87, It.IsAny<string>(), 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync((long _, string filter, int _, CancellationToken _) =>
            {
                var keyword = filter[5..].Split('|')[0];
                var rows = Enumerable.Range(1, 10).Select(i => new KnowledgeClaimRow(i, 87, ProposalGuidance.SourceTitle, i,
                    "p. " + i, "Rule", "Write about " + keyword, 1m, Array.Empty<string>(), null, null, null)).ToList();
                rows.Insert(0, rows[0] with { Id = 100, RetiredAtUtc = DateTimeOffset.UtcNow });
                rows.Insert(0, rows[0] with { Id = 101, Kind = "Fact", RetiredAtUtc = null });
                return (IReadOnlyList<KnowledgeClaimRow>)rows;
            });
        store.Setup(s => s.GetPassageAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgePassageText(1, ProposalGuidance.SourceTitle, "p. 1", "Original passage"));
        var guidance = new ProposalGuidance(store.Object);
        foreach (var block in Enum.GetValues<ProposalBlockType>())
        {
            var rows = await guidance.LoadAsync(block, CancellationToken.None);
            Assert.Equal(8, rows.Count);
            Assert.All(rows, row => { Assert.Equal("Rule", row.Claim.Kind); Assert.Null(row.Claim.RetiredAtUtc); Assert.StartsWith("(Shipley, p. ", row.Citation); });
            Assert.Equal("Original passage", (await guidance.PassageAsync(rows[0], CancellationToken.None))!.Text);
        }
        store.Verify(s => s.ListSourcesAsync(It.IsAny<CancellationToken>()), Times.Exactly(Enum.GetValues<ProposalBlockType>().Length));
        store.Verify(s => s.ListClaimsAsync(87, It.Is<string>(f => f.StartsWith("Rule:")), 8, It.IsAny<CancellationToken>()), Times.Exactly(Enum.GetValues<ProposalBlockType>().Length));
        store.Verify(s => s.GetPassageAsync(1, It.IsAny<CancellationToken>()), Times.Exactly(Enum.GetValues<ProposalBlockType>().Length));
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Brochure_GenerateEventAttachesOnce_ReportsFailure_AndUnsubscribes()
    {
        var path = Path.Combine(Path.GetTempPath(), "kor-brochure-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(path, PdfFixture());
        try
        {
            var builder = new BrochureEvents();
            var files = new Mock<IPursuitFileStore>(MockBehavior.Strict);
            var pursuits = new Mock<ICrmEngagementStore>(MockBehavior.Strict);
            pursuits.Setup(s => s.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new CrmEngagement { Id = 1, OpportunityId = 22 });
            var fail = false; var writes = 0;
            files.Setup(s => s.AddAsync(1, 22, Path.GetFileName(path), It.IsAny<Stream>(), It.IsAny<long>(), "application/pdf", "person", It.IsAny<CancellationToken>()))
                .Returns(async (long _, long? _, string name, Stream stream, long length, string? _, string _, CancellationToken ct) =>
                {
                    writes++;
                    if (fail) throw new IOException("Fixture upload failed");
                    using var copy = new MemoryStream(); await stream.CopyToAsync(copy, ct);
                    Assert.Equal(length, copy.Length); Assert.Equal(PdfFixture(), copy.ToArray());
                    return Attachment(name);
                });
            var messages = new List<string>();
            var launcher = new BrochurePursuitLauncher(Mock.Of<IServiceProvider>(), files.Object, pursuits.Object);
            using (launcher.Connect(builder, 22, 1, "person", messages.Add))
            {
                await builder.GenerateAsync(path);
                Assert.Equal(1, writes); Assert.StartsWith("Attached ", messages[0]);
                fail = true;
                await builder.GenerateAsync(path);
                Assert.Equal(2, writes); Assert.Contains("PDF was generated", messages[1]); Assert.Contains("could not be attached", messages[1]);
            }
            await builder.GenerateAsync(path);
            Assert.Equal(2, writes);
            var production = File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.App/Brochures/BrochureBuilderViewModel.Preview.cs"));
            Assert.True(production.IndexOf("RenderWithPreviewAsync(", StringComparison.Ordinal) < production.IndexOf("await NotifyProposalRenderedAsync(pdfPath)", StringComparison.Ordinal));
            Assert.Contains("AttachmentMessage", production);
        }
        finally { File.Delete(path); }
    }

    internal static PursuitRequirement Requirement(long id, string kind, string? parameter, string status = "Open")
        => new(id, 1, (int)id, kind == "Manual" ? "Include the signed declaration." : "Quoted RFP requirement " + id,
            "RFP Part III 3." + id, kind, parameter, status, null, null, null, "person", DateTimeOffset.UtcNow);
    internal static PursuitFile Attachment(string name) => new(10, 1, 22, name, null, null, null,
        "application/octet-stream", DateTimeOffset.UtcNow, "person", true);
    internal static RfpRateScheduleBlockContent FlaggedSchedule()
    {
        var model = new RfpRateScheduleBlockContent { Burden = .05m, StatutoryLoad = .12m, Overhead = .24m, Profit = .10m };
        model.Rows.Add(new RfpRateScheduleRow { Name = "Example Engineer", Position = "Design Lead", Scored = true, BaseHourly = 50, QuotedHourly = 84 });
        return model;
    }
    internal static byte[] DocxFixture(bool distinctBox = false)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            document.PackageProperties.Creator = "Author"; document.PackageProperties.LastModifiedBy = "Reviewer";
            document.PackageProperties.Revision = "7"; document.PackageProperties.Modified = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
            var main = document.AddMainDocumentPart();
            const string box = "<w:p><w:r><w:pict><v:shape><v:textbox><w:txbxContent><w:p><w:r><w:t>One textbox</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></w:r></w:p>";
            var xml = """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                  xmlns:v="urn:schemas-microsoft-com:vml" xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
                  <w:body><w:p><w:r><w:t>Body paragraph</w:t></w:r></w:p>
                    <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Table cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                    <w:p><w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:drawing><wps:wsp><wps:txbx><w:txbxContent><w:p><w:r><w:t>One textbox</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp></w:drawing></mc:Choice>
                    <mc:Fallback><w:pict><v:shape><v:textbox><w:txbxContent><w:p><w:r><w:t>One textbox</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>
                """ + (distinctBox ? box : "") + "</w:body></w:document>";
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml)); main.FeedData(input);
        }
        return stream.ToArray();
    }

    // A self-contained, three-page PDF; no external documents, services or PDF generator dependency.
    internal static byte[] PdfFixture()
    {
        const string content = "BT /F1 12 Tf 30 100 Td (Body paragraph) Tj ET\n";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R 5 0 R 7 0 R] /Count 3 >>"
        };
        for (var page = 0; page < 3; page++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 9 0 R >> >> /Contents {4 + page * 2} 0 R >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}endstream");
        }
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
    private sealed class BrochureEvents : IBrochureRenderEvents
    {
        public event Func<ProposalRenderedEvent, Task>? ProposalRendered;
        public async Task GenerateAsync(string path)
        {
            if (ProposalRendered is not { } handlers) return;
            foreach (Func<ProposalRenderedEvent, Task> handler in handlers.GetInvocationList()) await handler(new("proposal", path, "application/pdf"));
        }
    }
    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Org chart render did not finish.");
        Assert.Null(failure);
    }
    internal static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
