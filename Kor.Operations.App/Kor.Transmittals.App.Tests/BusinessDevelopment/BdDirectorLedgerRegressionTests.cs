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
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Opportunities;
using Moq;
using Xunit;
using Match = System.Text.RegularExpressions.Match;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Covers migration guards/batches/check values, allowed ledger mutation columns,
/// read-only account and target SQL contracts, interface immutability, mocked ask
/// mirroring/actuals/selection cancellation/ask filtering/Dashboard delivery, tabs, literals and dates.
/// Does not apply SQL, prove its execution plan or authorization, render dialogs,
/// or make a cross-store ask/mirror transaction. Same-class miss: SQL Server could
/// reject a CHECK whose expected text passes this gate. A literal scan also misses
/// developer wording supplied by a database binding or a composed runtime string.
/// </summary>
public sealed class BdDirectorLedgerRegressionTests
{
    private const string DirectorFolder = "Kor.Operations.App/BusinessDevelopment/Director/";
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Migration348_IsAdditiveIdempotentAndBatched()
    {
        var path = Path.Combine(RepoRoot(), "Kor.Opportunities.Data/Schema/348_BdDirectorLedger.sql");
        Check(File.Exists(path), "migration 348 exists", path);
        var sql = File.ReadAllText(path);
        foreach (var table in new[] { "BdTarget", "BdAsk", "BdDecision" })
            Contains(sql, $"IF OBJECT_ID(N'opportunities.{table}', N'U') IS NULL");
        Contains(sql, "SET QUOTED_IDENTIFIER ON;"); Contains(sql, "SET ANSI_NULLS ON;");
        Check(Regex.Matches(sql, @"\bEND\s+GO\b", RegexOptions.IgnoreCase).Count == 4,
            "four guarded table/index blocks each followed by GO", sql);
        // 'Drop' is an allowed decision kind, not a DDL statement.
        var statements = Regex.Replace(sql, @"N?'(?:''|[^'])*'", "''");
        Check(!Regex.IsMatch(statements, @"\b(USE|DROP|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase), "additive DDL without data changes or database selection", sql);
        CheckValues(sql, "Metric", BdTargetMetrics.All);
        CheckValues(sql, "Scope", new[] { "Firm", "Region", "Person" });
        CheckValues(sql, "Status", new[] { "Open", "Done", "Declined", "Withdrawn" });
        CheckValues(sql, "Kind", new[] { "Go", "NoGo", "Drop", "Reassign", "Price", "Target", "Other" });
        CheckValues(sql, "Outcome", new[] { "Pending", "Right", "Wrong", "Unclear" });
        Contains(sql, "Minutes BETWEEN 5 AND 180"); Contains(sql, "PeriodEndUtc > PeriodStartUtc");
        Contains(sql, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BdAsk_ToStaffKey_Status' AND object_id = OBJECT_ID(N'opportunities.BdAsk'))");
        Contains(sql, "(ToStaffKey, Status) INCLUDE (DueUtc)");
        Check(!sql.Contains("CASCADE", StringComparison.OrdinalIgnoreCase), "no cascading relationships", sql);
    }

    [Fact]
    public void LedgerStore_WritesOnlyItsThreeTables_AndNeverDeletes()
    {
        var source = Source("Kor.Opportunities.Data/Crm/SqlBdDirectorLedgerStore.cs");
        var writes = Regex.Matches(source, @"\b(?:INSERT\s+INTO|UPDATE)\s+(opportunities\.\w+)", RegexOptions.IgnoreCase).Cast<Match>().ToArray();
        Check(writes.Length == 6, "three inserts and three updates", writes.Length.ToString());
        foreach (var write in writes)
            Check(new[] { "opportunities.BdTarget", "opportunities.BdAsk", "opportunities.BdDecision" }.Contains(write.Groups[1].Value), "ledger-owned table", write.Value);
        Check(!Regex.IsMatch(source, @"\bDELETE\b", RegexOptions.IgnoreCase), "no deletion path", source);
        CheckSet(source, "BdTarget", new[] { "RetiredAtUtc", "RetiredBy" });
        CheckSet(source, "BdAsk", new[] { "Status", "ClosedAtUtc", "ClosedBy", "ClosedNote" });
        CheckSet(source, "BdDecision", new[] { "Outcome", "OutcomeNote", "ScoredAtUtc", "ScoredBy" });
        Contains(source, "WHERE Id = @id AND Status = N'Open'");
        Check(!source.Contains("AddWithValue", StringComparison.Ordinal), "typed parameters", "AddWithValue present");
    }

    [Fact]
    public void ReadStore_StaysSelectOnly_AndReadsTheAccountPopulation()
    {
        var source = Source("Kor.Opportunities.Data/Crm/SqlBdDirectorStore.cs");
        Check(!Regex.IsMatch(source, @"\b(INSERT|UPDATE|DELETE|MERGE)\b", RegexOptions.IgnoreCase), "read-only Director store", source);
        foreach (var clause in new[] { "co.ClendorClientId IS NOT NULL", "co.RetiredAtUtc IS NULL", "a.Status = N'Open'",
                     "SELECT TOP (@take)", "ORDER BY ISNULL(co.KorProjectsCount, 0) DESC, co.DisplayName",
                     "ISNULL(co.KorProjectsCountIsRecordsNotWins, 0), co.LastKorProjectAtUtc",
                     "reader.IsDBNull(6) ? null : reader.GetDateTime(6)" }) Contains(source, clause);
        // Ordinal six is the seventh selected column, a datetime2, not datetimeoffset.
        Check(Regex.IsMatch(source, @"SELECT TOP \(@take\) co.Id, co.DisplayName, co.Kind, co.ClendorClientId,\s*ISNULL\(co.KorProjectsCount, 0\), ISNULL\(co.KorProjectsCountIsRecordsNotWins, 0\), co.LastKorProjectAtUtc"),
            "last project at ordinal six", source);
    }

    [Fact]
    public void TargetActuals_UseOneParameterizedBatch_AndMetricSpecificPeriods()
    {
        var source = Source("Kor.Opportunities.Data/Crm/SqlBdDirectorStore.cs");
        var start = source.IndexOf("public async Task<BdTargetActuals> GetTargetActualsAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<IReadOnlyList<BdDirectorAccount>>", StringComparison.Ordinal);
        Check(start >= 0 && end > start, "target actuals method", source);
        var method = source[start..end];
        Check(Regex.Matches(method, "ExecuteReaderAsync").Count == 1, "one reader execution per batch", method);
        foreach (var token in new[] { "@m", "@s", "@k", "@ps", "@pe" }) Contains(method, "cmd.Parameters.Add(\"" + token + "\" + n,");
        Contains(method, "COALESCE(e.ExternalSource, N'') NOT LIKE N'Deltek.%'");
        Contains(method, "@m{n} = N'PursuitsOpened' AND e.OpenedAtUtc >= @ps{n} AND e.OpenedAtUtc < @pe{n}");
        Contains(method, "@m{n} <> N'PursuitsOpened' AND e.Stage IN (1, 3)");
        Contains(method, "@m{n} <> N'BuyersTouched' OR (w.LastTouchUtc >= @ps{n} AND w.LastTouchUtc < @pe{n})");
        Contains(method, "COLLATE SQL_Latin1_General_CP1_CI_AS");
    }

    [Fact]
    public void Decisions_AreImmutableExceptTheirOutcome()
    {
        var methods = typeof(IBdDirectorLedgerStore).GetMethods().Select(m => m.Name).ToArray();
        var forbidden = methods.Where(m => new[] { "Update", "Edit", "Delete", "Remove" }.Any(prefix => m.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
        Check(forbidden.Length == 0, "no text-editing or deletion methods", string.Join(", ", forbidden));
        Check(methods.Contains("ScoreDecisionAsync"), "ScoreDecisionAsync exists", string.Join(", ", methods));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ask_OnAPursuit_MirrorsToTheNextAction(bool linked)
    {
        var f = new Fixture();
        var ask = ExampleAsk(linked ? 1 : null);
        var original = new CrmEngagement { Id = 1, Notes = "Keep this", RowVersion = new byte[] { 1, 2 } };
        f.Records.Setup(s => s.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(original);
        f.Records.Setup(s => s.UpdateAsync(It.IsAny<CrmEngagement>(), "Ian", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CrmEngagement row, string actor, CancellationToken ct) => row);
        using var vm = f.Create();
        await vm.CreateAskAsync(ask, "Ian", CancellationToken.None);
        f.Ledger.Verify(s => s.CreateAskAsync(It.Is<BdAsk>(a => a.Ask == ask.Ask && a.AskedBy == "Ian"), It.IsAny<CancellationToken>()), Times.Once);
        if (linked)
            f.Records.Verify(s => s.UpdateAsync(It.Is<CrmEngagement>(r => r.Id == 1 && r.NextActionNote == ask.Ask
                && r.NextActionDueUtc == ask.DueUtc && r.Notes == original.Notes && r.RowVersion == original.RowVersion), "Ian", It.IsAny<CancellationToken>()), Times.Once);
        else
            f.Records.Verify(s => s.UpdateAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Check(vm.StatusMessage == "Ask saved.", "Ask saved.", vm.StatusMessage);
    }

    [Fact]
    public async Task Ask_MirrorFailureKeepsTheSavedRecord_AndSaysSo()
    {
        var f = new Fixture();
        f.Records.Setup(s => s.GetByIdAsync(1, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("unavailable"));
        using var vm = f.Create();
        await vm.CreateAskAsync(ExampleAsk(1), "Ian", CancellationToken.None);
        f.Ledger.Verify(s => s.CreateAskAsync(It.IsAny<BdAsk>(), It.IsAny<CancellationToken>()), Times.Once);
        Check(vm.StatusMessage.StartsWith("Ask saved. The pursuit's next action could not be updated", StringComparison.Ordinal),
            "saved ask with explicit mirror failure", vm.StatusMessage);
    }

    [Fact]
    public async Task Targets_ActualsComeFromTheReadStore()
    {
        var f = new Fixture();
        var targets = new[] { ExampleTarget(BdTargetMetrics.LivePursuits), ExampleTarget("FutureMetric") with { Id = 2 } };
        f.Ledger.Setup(s => s.ListTargetsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(targets);
        IReadOnlyList<BdTargetKey>? seen = null;
        f.Reads.Setup(s => s.GetTargetActualsAsync(It.IsAny<IReadOnlyList<BdTargetKey>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<BdTargetKey>, CancellationToken>((keys, _) => seen = keys)
            .ReturnsAsync((IReadOnlyList<BdTargetKey> keys, CancellationToken _) =>
                new BdTargetActuals(keys.ToDictionary(k => k, k => k.Metric == BdTargetMetrics.LivePursuits ? 101m : 7m), 101, 0));
        using var vm = f.Create();
        await vm.LoadAsync(CancellationToken.None);
        var expected = targets.Select(t => new BdTargetKey(t.Metric, t.Scope, t.ScopeKey, t.PeriodStartUtc, t.PeriodEndUtc)).ToArray();
        Check(seen is not null && seen.SequenceEqual(expected), "one complete key per target", seen is null ? "no call" : seen.Count.ToString());
        Check(vm.Targets.Count == 2 && vm.Targets[0].Actual == 101 && vm.Targets[1].Actual == 7,
            "returned actuals 101 and 7", string.Join(",", vm.Targets.Select(t => t.Actual)));
        Check(vm.Targets[1].MetricDisplay == "Unknown metric", "Unknown metric", vm.Targets[1].MetricDisplay);
        Check(vm.FeeCoverage == "101 live pursuits; 0 carry a fee.", "measured fee coverage", vm.FeeCoverage);
        f.Reads.Verify(s => s.GetTargetActualsAsync(It.IsAny<IReadOnlyList<BdTargetKey>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Accounts_ReadDeltekForTheSelectedAccountOnly()
    {
        var f = new Fixture();
        f.Deltek.Setup(s => s.LoadAsync("client-1", It.IsAny<CancellationToken>())).ReturnsAsync(new DeltekClientIntelligence { ClientName = "First" });
        f.Deltek.Setup(s => s.LoadAsync("client-2", It.IsAny<CancellationToken>())).ReturnsAsync((DeltekClientIntelligence?)null);
        using var vm = f.Create();
        await vm.LoadAsync(CancellationToken.None);
        f.Deltek.Verify(s => s.LoadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        Check(vm.Accounts.Count == 2, "two accounts loaded", vm.Accounts.Count.ToString());
        vm.SelectedAccount = vm.Accounts[0];
        await vm.AccountLoadTask;
        f.Deltek.Verify(s => s.LoadAsync("client-1", It.IsAny<CancellationToken>()), Times.Once);
        vm.SelectedAccount = vm.Accounts[1];
        await vm.AccountLoadTask;
        f.Deltek.Verify(s => s.LoadAsync("client-2", It.IsAny<CancellationToken>()), Times.Once);
        Check(vm.AccountMessage == "No Deltek record could be read for this client.", "no-record sentence", vm.AccountMessage);
    }

    [Fact]
    public async Task Accounts_IgnoreAStaleSelectionResponse()
    {
        var f = new Fixture();
        var first = new TaskCompletionSource<DeltekClientIntelligence?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        f.Deltek.Setup(s => s.LoadAsync("client-1", It.IsAny<CancellationToken>()))
            .Callback<string?, CancellationToken>((_, ct) => firstToken = ct).Returns(first.Task);
        f.Deltek.Setup(s => s.LoadAsync("client-2", It.IsAny<CancellationToken>())).ReturnsAsync(new DeltekClientIntelligence { ClientName = "Second" });
        using var vm = f.Create();
        await vm.LoadAsync(CancellationToken.None);
        vm.SelectedAccount = vm.Accounts[0];
        var firstRead = vm.AccountLoadTask;
        vm.SelectedAccount = vm.Accounts[1];
        await vm.AccountLoadTask;
        Check(firstToken.IsCancellationRequested, "previous selection cancelled", firstToken.IsCancellationRequested.ToString());
        first.SetResult(new DeltekClientIntelligence { ClientName = "First" });
        await firstRead;
        Check(vm.SelectedAccountDeltek?.ClientName == "Second", "Second remains selected", vm.SelectedAccountDeltek?.ClientName ?? "null");
    }

    [Fact]
    public async Task Asks_FilterChangeDuringRefresh_KeepsNewestRowsAndFinishesRefresh()
    {
        var f = new Fixture();
        var open = new TaskCompletionSource<IReadOnlyList<BdAsk>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken openToken = default;
        f.Ledger.Setup(s => s.ListAsksAsync("Open", It.IsAny<CancellationToken>()))
            .Callback<string?, CancellationToken>((_, ct) => openToken = ct).Returns(open.Task);
        f.Ledger.Setup(s => s.ListAsksAsync("Done", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ExampleAsk(null) with { Status = "Done" } });
        using var vm = f.Create();
        var refresh = vm.LoadAsync(CancellationToken.None);
        vm.AskStatusFilter = "Done";
        await vm.AsksLoadTask;
        Check(openToken.IsCancellationRequested, "superseded Open read cancelled", openToken.IsCancellationRequested.ToString());
        open.SetResult(new[] { ExampleAsk(null) });
        await refresh;
        Check(vm.Asks.Count == 1 && vm.Asks[0].Record.Status == "Done", "Done filter retained", string.Join(",", vm.Asks.Select(a => a.Record.Status)));
        Check(!vm.IsLoading && vm.StatusMessage == "Up to date.", "window refresh completed", vm.StatusMessage);
        f.Ledger.Verify(s => s.ListDecisionsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dashboard_ShowsOpenAsksForTheSignedInPerson()
    {
        const string actor = "ilalonde@korstructural.com";
        var ledger = new Mock<IBdDirectorLedgerStore>();
        ledger.Setup(s => s.ListOpenAsksForAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { ExampleAsk(null) });
        var vm = Dashboard(ledger.Object);
        await vm.LoadAsksAsync(actor, CancellationToken.None);
        ledger.Verify(s => s.ListOpenAsksForAsync(actor, It.IsAny<CancellationToken>()), Times.Once);
        Check(vm.HasAsks && vm.AskedOfMe.Count == 1 && vm.AskedOfMe[0].Ask == ExampleAsk(null).Ask,
            "one open ask for the signed-in person", vm.AskedOfMe.Count.ToString());
        await vm.CloseAskAsync(1, "Done", "Done today", actor);
        ledger.Verify(s => s.CloseAskAsync(1, "Done", "Done today", actor, It.IsAny<CancellationToken>()), Times.Once);
        Check(!vm.HasAsks && vm.AskedOfMe.Count == 0, "closed ask removed", vm.AskedOfMe.Count.ToString());
        var withoutLedger = Dashboard(null);
        await withoutLedger.LoadAsksAsync(actor, CancellationToken.None);
        await withoutLedger.CloseAskAsync(1, "Done", null, actor);
        Check(!withoutLedger.HasAsks, "optional ledger leaves no asks", withoutLedger.AskedOfMe.Count.ToString());
    }

    [Fact]
    public void Window_HasTheFiveTabs_InOrder_AndPlainWords()
    {
        var xaml = Source(DirectorFolder + "BdDirectorWindow.xaml");
        var headers = XDocument.Parse(xaml).Descendants().Where(e => e.Name.LocalName == "TabItem").Select(e => (string?)e.Attribute("Header")).ToArray();
        Check(headers.SequenceEqual(new[] { "The number", "People", "Funnel", "Accounts", "Asks & decisions" }),
            "five tabs in the specified order", string.Join(", ", headers));
        foreach (var caption in new[] { "Number", "People", "Funnel", "Accounts", "Asks", "Decisions" })
            Contains(xaml, "{x:Static director:BdDirectorViewModel." + caption + "Caption}");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), DirectorFolder)))
        {
            var source = File.ReadAllText(file);
            IEnumerable<string> literals;
            if (Path.GetExtension(file) == ".xaml")
            {
                var doc = XDocument.Parse(source);
                literals = doc.Descendants().Attributes().Where(a => new[] { "Text", "Content", "Header", "ToolTip", "Title", "Placeholder.Text" }.Contains(a.Name.LocalName))
                    .Select(a => a.Value).Where(v => !v.StartsWith("{", StringComparison.Ordinal));
                foreach (var combo in doc.Descendants().Where(e => e.Name.LocalName == "ComboBox"))
                    Check(combo.Attributes().Any(a => new[] { "SelectedIndex", "SelectedItem", "SelectedValue", "Placeholder.Text" }.Contains(a.Name.LocalName)),
                        "ComboBox selection or placeholder", combo.ToString());
            }
            else if (Path.GetExtension(file) == ".cs")
            {
                source = Regex.Replace(source, @"//[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
                literals = Regex.Matches(source, "\"((?:\\\\.|[^\"\\\\])*)\"").Cast<Match>().Select(m => m.Groups[1].Value);
            }
            else continue;
            foreach (var literal in literals)
                Check(!Regex.IsMatch(literal, @"\b(Overwatch|Bazaar|engagement|CRM|canonical|UPN|staff id)\b", RegexOptions.IgnoreCase),
                    "plain visible words in " + Path.GetFileName(file), literal);
        }
    }

    [Fact]
    public void Director_StillContributesZeroInlineDates()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), DirectorFolder)))
        {
            if (Path.GetExtension(file) is not ".cs" and not ".xaml") continue;
            var count = Regex.Matches(File.ReadAllText(file), "yyyy-MM-dd|MMM d, yyyy").Count;
            Check(count == 0, "zero inline date formats in " + Path.GetFileName(file), count.ToString());
        }
        // Read from git show dcfe00a9:.../DashboardView.xaml before editing: zero.
        var dashboardCount = Regex.Matches(Source("Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml"), "yyyy-MM-dd|MMM d, yyyy").Count;
        Check(dashboardCount == 0, "Dashboard baseline of zero at dcfe00a9", dashboardCount.ToString());
    }

    private static BdAsk ExampleAsk(long? pursuit) => new(1, "person@example.com", pursuit, null,
        "Confirm the meeting date.", "The next step is waiting.", 60, Now.AddDays(7), "", Now, "Open", null, null, null);
    private static BdTarget ExampleTarget(string metric) => new(1, metric, "Firm", null, Now, Now.AddMonths(3), 120, null, "Ian", Now);
    private static DashboardViewModel Dashboard(IBdDirectorLedgerStore? ledger) => new(Mock.Of<IOpportunityStore>(), Mock.Of<IBdDashboardStore>(),
        new IntelReadService("Server=none;Database=none"), new IntelPersistenceService("Server=none;Database=none"), ledger: ledger);

    private sealed class Fixture
    {
        internal readonly Mock<IBdDirectorStore> Reads = new();
        internal readonly Mock<IBdDirectorLedgerStore> Ledger = new();
        internal readonly Mock<ICrmEngagementStore> Records = new();
        internal readonly Mock<IDeltekClientContextService> Deltek = new();
        internal readonly Mock<IPursuitOverwatchStore> Pursuits = new();
        internal readonly Mock<IBdAttributionStore> Attribution = new();
        internal Fixture()
        {
            Reads.Setup(s => s.GetPeopleAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDirectorPerson>());
            Reads.Setup(s => s.GetFunnelAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BdDirectorFunnel(new Dictionary<int, double>(), Array.Empty<BdDirectorUndecidedPursuit>(), 0, 0, 0, 0));
            Reads.Setup(s => s.GetTargetActualsAsync(It.IsAny<IReadOnlyList<BdTargetKey>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BdTargetActuals(new Dictionary<BdTargetKey, decimal>(), 0, 0));
            Reads.Setup(s => s.GetAccountsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[]
            {
                new BdDirectorAccount(1, "First", "KorClient", "client-1", 2, true, null, 0, null, 0),
                new BdDirectorAccount(2, "Second", "KorClient", "client-2", 1, true, null, 0, null, 0),
            });
            Ledger.Setup(s => s.ListTargetsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdTarget>());
            Ledger.Setup(s => s.ListAsksAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdAsk>());
            Ledger.Setup(s => s.ListDecisionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDecision>());
            Records.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CrmEngagement>());
            Pursuits.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<PursuitOverwatchRow>());
            Attribution.Setup(s => s.GetSummaryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BdAttributionSummary(0, 0, 0, 0, 0, Array.Empty<AttributionSourceRow>(), Array.Empty<AttributionOwnerRow>()));
        }
        internal BdDirectorViewModel Create() => new(Reads.Object, Pursuits.Object, Records.Object, Attribution.Object, Ledger.Object, Deltek.Object);
    }

    private static void CheckValues(string sql, string column, IEnumerable<string> expected)
    {
        var match = Regex.Match(sql, @"CHECK\s*\(\s*" + column + @"\s+IN\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
        Check(match.Success, column + " CHECK", "missing");
        var actual = Regex.Matches(match.Groups[1].Value, "N'([^']*)'").Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(v => v).ToArray();
        Check(actual.SequenceEqual(expected.OrderBy(v => v)), column + " allowed values", string.Join(", ", actual));
    }
    private static void CheckSet(string source, string table, IEnumerable<string> expected)
    {
        var match = Regex.Match(source, @"UPDATE opportunities\." + table + @"\s+SET\s+(.*?)\s+WHERE", RegexOptions.Singleline);
        Check(match.Success, table + " update", "missing");
        var actual = Regex.Matches(match.Groups[1].Value, @"(?:^|,)\s*(\w+)\s*=").Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(v => v).ToArray();
        Check(actual.SequenceEqual(expected.OrderBy(v => v)), table + " permitted mutation columns", string.Join(", ", actual));
    }
    private static void Contains(string source, string expected) => Check(source.Contains(expected, StringComparison.Ordinal), expected, "missing from source");
    private static void Check(bool condition, string expected, string actual) => Assert.True(condition, $"EXPECTED {expected}; ACTUAL {actual}");
    private static string Source(string path) => File.ReadAllText(Path.Combine(RepoRoot(), path));
    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("EXPECTED repository root; ACTUAL missing");
    }
}
