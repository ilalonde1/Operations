#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Covers migration 350 guards and additive scope, review-only SQL columns, six tabs,
/// captions/plain literals/date formats, actor/reason forwarding, isolated count refresh,
/// sampling/concept reads and cancellation of stale claims/passages with mocked stores.
/// Does not execute SQL, prove permissions/locking/plans, read any real book or passage,
/// open dialogs, render WPF or prove a claim's meaning/support. Same-class miss: a join
/// could duplicate claim rows while retaining every SQL clause this source gate checks.
/// Synthetic row text is illustrative only; rendering belongs to BdScreensRenderTests.
/// </summary>
public sealed class BdDirectorKnowledgeRegressionTests
{
    private const string Director = "Kor.Operations.App/BusinessDevelopment/Director/";

    [Fact]
    public void Migration350_IsAdditiveAndGuarded()
    {
        var sql = WithoutComments(Source("Kor.Opportunities.Data/Schema/350_KnowledgeClaimsAndConcepts.sql"));
        foreach (var table in new[] { "Concept", "ClaimConcept", "ClaimImport" })
        {
            Contains(sql, $"IF OBJECT_ID(N'knowledge.{table}', N'U') IS NULL");
            Contains(sql, $"CREATE TABLE knowledge.{table}");
        }
        foreach (var column in new[] { "ReviewedAtUtc", "ReviewedBy" }) Contains(sql, $"COL_LENGTH(N'knowledge.Claim', N'{column}') IS NULL");
        foreach (var column in new[] { "ClaimsExtractedAtUtc", "ClaimsExtractedBy" }) Contains(sql, $"COL_LENGTH(N'knowledge.Passage', N'{column}') IS NULL");
        Contains(sql, "SET QUOTED_IDENTIFIER ON;"); Contains(sql, "SET ANSI_NULLS ON;");
        Contains(sql, "UX_Knowledge_Concept_Slug UNIQUE (Slug)");
        Contains(sql, "PRIMARY KEY (ClaimId, ConceptId)");
        Contains(sql, "CHECK (Confidence BETWEEN 0 AND 1)");
        Contains(sql, "CHECK (LinkedBy IN (N'Model', N'Human'))");
        Contains(sql, "REFERENCES knowledge.Claim(Id)"); Contains(sql, "REFERENCES knowledge.Concept(Id)");
        Contains(sql, "REFERENCES knowledge.Source(Id)");
        Contains(sql, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Knowledge_ClaimConcept_Concept'");
        Contains(sql, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Knowledge_Passage_Unextracted'");
        Contains(sql, "ON knowledge.Passage (SourceId, Ordinal) WHERE ClaimsExtractedAtUtc IS NULL;");
        foreach (var column in new[] { "FileName NVARCHAR(260)", "FileSha256 CHAR(64)", "ImportedBy NVARCHAR(100)",
                     "PassagesCovered INT", "ClaimsWritten INT", "ClaimsRejected INT", "Forced BIT" }) Contains(sql, column);
        var batches = Regex.Split(sql, @"(?im)^GO\s*$").Where(b => !string.IsNullOrWhiteSpace(b)).ToArray();
        Check(batches.Length == 8, "settings, three tables, two column batches, two indexes", batches.Length.ToString());
        Check(!Regex.IsMatch(sql, @"\b(USE|DROP|TRUNCATE|GRANT|DELETE)\b|\bINSERT\s+INTO\b", RegexOptions.IgnoreCase),
            "no database selection, destructive commands, grants or data", "forbidden SQL found");
    }

    [Fact]
    public void ClaimStore_NeverInsertsDeletesOrRewords()
    {
        var source = WithoutComments(Source("Kor.Opportunities.Data/Knowledge/SqlKnowledgeClaimStore.cs"));
        Check(!Regex.IsMatch(source, @"\b(INSERT|DELETE|MERGE)\b", RegexOptions.IgnoreCase), "review-only writes", "forbidden write");
        Check(!Regex.IsMatch(source, @"\bStatement\s*=", RegexOptions.IgnoreCase), "immutable statement", "assignment found");
        var updates = Regex.Matches(source, @"UPDATE\s+(\w+\.\w+)\s+SET\s+(.*?)\s+WHERE", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Check(updates.Count == 2, "two review updates", updates.Count.ToString());
        var columns = new[] { new[] { "ReviewedAtUtc", "ReviewedBy" }, new[] { "RetiredAtUtc", "RetiredReason", "ReviewedAtUtc", "ReviewedBy" } };
        for (var i = 0; i < updates.Count; i++)
        {
            Check(updates[i].Groups[1].Value == "knowledge.Claim", "knowledge.Claim update", updates[i].Value);
            var actual = Regex.Matches(updates[i].Groups[2].Value, @"(?:^|,)\s*(\w+)\s*=").Select(m => m.Groups[1].Value).ToArray();
            Check(actual.SequenceEqual(columns[i]), "permitted review columns", string.Join(",", actual));
        }
        Contains(source, "WHERE Id = @id AND RetiredAtUtc IS NULL;");
        Contains(source, "ORDER BY NEWID()");
        Contains(source, "SqlDbType.NVarChar, 200");
        Check(!source.Contains("AddWithValue", StringComparison.Ordinal), "typed parameters", "inferred parameter found");
        var contract = Source("Kor.Opportunities.Data/Knowledge/IKnowledgeClaimStore.cs");
        Check(!contract.Contains("Kor.Opportunities.Knowledge", StringComparison.Ordinal), "Data contract without the ingestion library", "unexpected dependency");
    }

    [Fact]
    public void Window_HasEightTabs_KnowledgeSixth_AndPlainWords()
    {
        var xaml = Source(Director + "BdDirectorWindow.xaml");
        var document = XDocument.Parse(xaml);
        var tabs = document.Descendants().Where(e => e.Name.LocalName == "TabItem").Select(e => (string?)e.Attribute("Header")).ToArray();
        Check(tabs.SequenceEqual(new[] { "The number", "People", "Funnel", "Accounts", "Asks & decisions", "Knowledge", "Market", "This week" }),
            "eight ordered tabs with Knowledge sixth", string.Join(",", tabs));
        const string caption = "What the books assert, one sentence each, cited to the passage it rests on. Keep what holds for KOR; retire what does not. Nothing is deleted.";
        Check(BdDirectorViewModel.KnowledgeCaption == caption, "exact Knowledge caption", BdDirectorViewModel.KnowledgeCaption);
        Contains(xaml, "{x:Static director:BdDirectorViewModel.KnowledgeCaption}");
        var claimGrid = document.Descendants().Single(e => e.Name.LocalName == "DataGrid" && (string?)e.Attribute("ItemsSource") == "{Binding KnowledgeClaims}");
        Check((string?)claimGrid.Attribute("RowHeight") == "NaN", "automatic claim row height", claimGrid.ToString());
        Check(!claimGrid.Descendants().Any(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") is "Height" or "MaxHeight"),
            "no clipping height on claim rows", claimGrid.ToString());
        Contains(xaml, "Text=\"{Binding SelectedPassage.Text, Mode=OneWay}\" IsReadOnly=\"True\" TextWrapping=\"Wrap\"");
        var window = Source(Director + "BdDirectorWindow.xaml.cs");
        Contains(window, "_vm.KeepClaimAsync(row, _actor, ct)");
        Contains(window, "_vm.RetireClaimAsync(row, _actor, dialog.Note!, ct)");
        Contains(window, "200, required: true");
        foreach (var file in DirectorFiles())
        {
            var text = File.ReadAllText(file);
            var literals = Path.GetExtension(file) == ".xaml"
                ? XDocument.Parse(text).Descendants().Attributes()
                    .Where(a => new[] { "Text", "Content", "Header", "ToolTip", "Title" }.Contains(a.Name.LocalName))
                    .Select(a => a.Value).Where(v => !v.StartsWith("{", StringComparison.Ordinal))
                : Regex.Matches(WithoutComments(text), "\"((?:\\\\.|[^\"\\\\])*)\"").Select(m => m.Groups[1].Value);
            foreach (var literal in literals)
                Check(!Regex.IsMatch(literal, @"\b(Overwatch|Bazaar|engagement|CRM|canonical|UPN|staff id)\b", RegexOptions.IgnoreCase),
                    "plain visible words in " + Path.GetFileName(file), literal);
        }
    }

    [Fact]
    public async Task Review_KeepAndRetire_ForwardActorAndReason_AndReloadCounts()
    {
        var fixture = new Fixture();
        using var vm = fixture.Create();
        await vm.LoadKnowledgeAsync(CancellationToken.None);
        await vm.PassageLoadTask;
        Check(vm.ClaimFilter == "Unreviewed" && vm.SelectedPassage?.Id == 101, "default unreviewed claims with cited passage", vm.ClaimFilter);
        var firstBook = vm.KnowledgeBooks[0];
        var otherBook = vm.KnowledgeBooks[1];
        var kept = vm.KnowledgeClaims[0];
        await vm.KeepClaimAsync(kept, "actor@example.com", CancellationToken.None);
        fixture.Claims.Verify(s => s.KeepAsync(1, "actor@example.com", It.IsAny<CancellationToken>()), Times.Once);
        Check(firstBook.Source.Reviewed == 1 && vm.KnowledgeClaims.Count == 1, "kept count refreshed and kept row leaves unreviewed", firstBook.Source.ToString());
        var retired = vm.KnowledgeClaims[0];
        await vm.RetireClaimAsync(retired, "actor@example.com", "  Not suitable for this firm.  ", CancellationToken.None);
        fixture.Claims.Verify(s => s.RetireAsync(2, "actor@example.com", "Not suitable for this firm.", It.IsAny<CancellationToken>()), Times.Once);
        Check(firstBook.Source.Reviewed == 2 && firstBook.Source.Retired == 1 && vm.KnowledgeClaims.Count == 0,
            "retired counts and current list refreshed", firstBook.Source.ToString());
        Check(ReferenceEquals(firstBook, vm.KnowledgeBooks[0]) && ReferenceEquals(otherBook, vm.KnowledgeBooks[1]) && otherBook.Source.Reviewed == 0,
            "existing book rows retained and unrelated book untouched", "row replaced");
        fixture.Claims.Verify(s => s.ListSourcesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
        fixture.Claims.Verify(s => s.ListConceptsAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Claims.Verify(s => s.ListClaimsAsync(4, "Unreviewed", 1000, It.IsAny<CancellationToken>()), Times.Exactly(3));
        // Every other store is strict and has no setup: a whole-window reload would fail this gate.
        foreach (var reason in new[] { "", " ", new string('a', 201) })
            await Assert.ThrowsAsync<ArgumentException>(() => vm.RetireClaimAsync(kept, "actor@example.com", reason, CancellationToken.None));
        vm.ClaimFilter = "Retired";
        await vm.ClaimsLoadTask;
        Check(vm.KnowledgeClaims.Single().Status == "Retired", "retired filter", vm.KnowledgeClaims.Single().Status);
        await vm.KeepClaimAsync(vm.KnowledgeClaims.Single(), "actor@example.com", CancellationToken.None);
        fixture.Claims.Verify(s => s.KeepAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Knowledge_SamplesTwenty_AndGroupsByConcept_WithPassagesReachable()
    {
        var fixture = new Fixture();
        using var vm = fixture.Create();
        await vm.LoadKnowledgeAsync(CancellationToken.None);
        await vm.SampleKnowledgeAsync();
        fixture.Claims.Verify(s => s.SampleUnreviewedAsync(4, 20, It.IsAny<CancellationToken>()), Times.Once);
        vm.SelectedConcept = vm.KnowledgeConcepts[0];
        await vm.ClaimsLoadTask;
        await vm.PassageLoadTask;
        fixture.Claims.Verify(s => s.ListClaimsByConceptAsync(10, 1000, It.IsAny<CancellationToken>()), Times.Once);
        Check(vm.ClaimsCaption == "Concept · Qualify before investing · 2 claims from 1 books", "concept count caption", vm.ClaimsCaption);
        Check(vm.SelectedPassage?.Id == vm.SelectedClaim?.Claim.PassageId, "selected claim's cited passage reachable", "passage differs");
        await vm.KeepClaimAsync(vm.KnowledgeClaims[0], "reviewer", CancellationToken.None);
        fixture.Claims.Verify(s => s.ListClaimsByConceptAsync(10, 1000, It.IsAny<CancellationToken>()), Times.Exactly(2));
        vm.ClaimFilter = "Kept";
        await vm.ClaimsLoadTask;
        Check(vm.SelectedConcept is null && vm.KnowledgeClaims.Single().Status == "Kept", "book filter clears concept and shows kept", vm.ClaimsCaption);
    }

    [Fact]
    public async Task Knowledge_DiscardsStaleClaimsAndPassages()
    {
        var fixture = new Fixture();
        var oldClaims = new TaskCompletionSource<IReadOnlyList<KnowledgeClaimRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldPassage = new TaskCompletionSource<KnowledgePassageText?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken claimsToken = default, passageToken = default;
        using var vm = fixture.Create();
        await vm.LoadKnowledgeAsync(CancellationToken.None);
        fixture.Claims.Setup(s => s.ListClaimsAsync(4, "All", 1000, It.IsAny<CancellationToken>()))
            .Callback<long, string, int, CancellationToken>((id, filter, take, ct) => claimsToken = ct).Returns(oldClaims.Task);
        vm.ClaimFilter = "All";
        var staleList = vm.ClaimsLoadTask;
        vm.ClaimFilter = "Unreviewed";
        await vm.ClaimsLoadTask;
        oldClaims.SetResult(Array.Empty<KnowledgeClaimRow>());
        await staleList;
        Check(claimsToken.IsCancellationRequested && vm.KnowledgeClaims.Count == 2, "stale list discarded", vm.KnowledgeClaims.Count.ToString());
        fixture.Claims.Setup(s => s.GetPassageAsync(101, It.IsAny<CancellationToken>()))
            .Callback<long, CancellationToken>((id, ct) => passageToken = ct).Returns(oldPassage.Task);
        vm.SelectedClaim = null;
        vm.SelectedClaim = vm.KnowledgeClaims[0];
        var staleText = vm.PassageLoadTask;
        vm.SelectedClaim = vm.KnowledgeClaims[1];
        await vm.PassageLoadTask;
        oldPassage.SetResult(new KnowledgePassageText(101, "Synthetic", "old", "Old synthetic text."));
        await staleText;
        Check(passageToken.IsCancellationRequested && vm.SelectedPassage?.Id == 102, "stale passage discarded", vm.SelectedPassage?.Id.ToString() ?? "none");
    }

    [Fact]
    public void Director_StillContributesZeroInlineDates()
    {
        foreach (var file in DirectorFiles())
        {
            var count = Regex.Matches(File.ReadAllText(file), "yyyy-MM-dd|MMM d, yyyy").Count;
            Check(count == 0, "zero inline date formats in " + Path.GetFileName(file), count.ToString());
        }
    }

    private sealed class Fixture
    {
        internal readonly Mock<IKnowledgeClaimStore> Claims = new(MockBehavior.Strict);
        private readonly List<KnowledgeClaimRow> _rows = new()
        {
            new(1, 4, "Synthetic book", 101, "Illustrative A", "Rule", "Illustrative: confirm a need before preparing a proposal.", 0.9m, new[] { "Qualify before investing" }, null, null, null),
            new(2, 4, "Synthetic book", 102, "Illustrative B", "Heuristic", "Illustrative: discuss the likely effort before assigning time.", 0.8m, new[] { "Qualify before investing" }, null, null, null),
        };
        internal Fixture()
        {
            Claims.Setup(s => s.ListSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new[]
            {
                new KnowledgeSourceSummary(4, "Synthetic book", "Example author", 663, 2, _rows.Count, _rows.Count(r => r.ReviewedAtUtc is not null), _rows.Count(r => r.RetiredAtUtc is not null)),
                new KnowledgeSourceSummary(11, "Other synthetic book", "Example author", 316, 0, 0, 0, 0),
            });
            Claims.Setup(s => s.ListConceptsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new KnowledgeConceptRow(10, "qualify-before-investing", "Qualify before investing", 2, 1) });
            Claims.Setup(s => s.ListClaimsAsync(4, It.IsAny<string>(), 1000, It.IsAny<CancellationToken>()))
                .ReturnsAsync((long id, string filter, int take, CancellationToken ct) => (IReadOnlyList<KnowledgeClaimRow>)_rows.Where(r => filter switch
                {
                    "Unreviewed" => r.ReviewedAtUtc is null && r.RetiredAtUtc is null,
                    "Kept" => r.ReviewedAtUtc is not null && r.RetiredAtUtc is null,
                    "Retired" => r.RetiredAtUtc is not null,
                    _ => true,
                }).ToArray());
            Claims.Setup(s => s.SampleUnreviewedAsync(4, 20, It.IsAny<CancellationToken>())).ReturnsAsync(() => _rows.ToArray());
            Claims.Setup(s => s.ListClaimsByConceptAsync(10, 1000, It.IsAny<CancellationToken>())).ReturnsAsync(() => _rows.ToArray());
            Claims.Setup(s => s.GetPassageAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long id, CancellationToken ct) => new KnowledgePassageText(id, "Synthetic book", "Illustrative", "Illustrative passage text, not from a book."));
            Claims.Setup(s => s.KeepAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<long, string, CancellationToken>((id, actor, ct) =>
                {
                    var i = _rows.FindIndex(r => r.Id == id);
                    _rows[i] = _rows[i] with { ReviewedAtUtc = DateTimeOffset.UtcNow };
                }).Returns(Task.CompletedTask);
            Claims.Setup(s => s.RetireAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<long, string, string, CancellationToken>((id, actor, reason, ct) =>
                {
                    var i = _rows.FindIndex(r => r.Id == id);
                    _rows[i] = _rows[i] with { ReviewedAtUtc = DateTimeOffset.UtcNow, RetiredAtUtc = DateTimeOffset.UtcNow, RetiredReason = reason };
                }).Returns(Task.CompletedTask);
        }
        internal BdDirectorViewModel Create() => new(new Mock<IBdDirectorStore>(MockBehavior.Strict).Object,
            new Mock<IPursuitOverwatchStore>(MockBehavior.Strict).Object, new Mock<ICrmEngagementStore>(MockBehavior.Strict).Object,
            new Mock<IBdAttributionStore>(MockBehavior.Strict).Object, new Mock<IBdDirectorLedgerStore>(MockBehavior.Strict).Object,
            new Mock<IDeltekClientContextService>(MockBehavior.Strict).Object, Claims.Object,
            DirectorMarketDefaults.Dashboard(), DirectorMarketDefaults.Opportunities(), DirectorMarketDefaults.Brief());
    }

    private static string WithoutComments(string source) => Regex.Replace(source, @"//[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
    private static void Contains(string source, string expected) => Check(source.Contains(expected, StringComparison.Ordinal), expected, "missing from source");
    private static void Check(bool condition, string expected, string actual) => Assert.True(condition, $"EXPECTED {expected}; ACTUAL {actual}");
    private static IEnumerable<string> DirectorFiles() => Directory.EnumerateFiles(Path.Combine(RepoRoot(), Director))
        .Where(f => Path.GetExtension(f) is ".cs" or ".xaml");
    private static string Source(string path) => File.ReadAllText(Path.Combine(RepoRoot(), path));
    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
