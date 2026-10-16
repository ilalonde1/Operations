#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Services;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Moq;
using Xunit;
using Match = System.Text.RegularExpressions.Match;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Covers the role/config/fallback wiring, visible literal wording and captions,
/// SQL population/join/grouping contracts, date-ratchet inclusion, alias-aware drill
/// filtering, analytics segmentation, source attribution and guarded reassignment.
/// Also checks migration 352 and the brief store's two inserts and acknowledgement update.
/// 9g adds the bid read, migration 353, exact submission write columns and stage/status mapping.
/// Person grouping is asserted in SQL (the store already returns one row/person),
/// not by feeding duplicate person aggregates into the view model.
/// Does not execute SQL, authenticate a user, render WPF, open dialogs or prove
/// actual database cardinalities. Same-class fault missed: an extra SQL join could
/// multiply counts while retaining every required clause. Bound buyer/project data
/// can contain developer words even though the screen's own literals do not.
/// </summary>
public sealed class BdDirectorRegressionTests
{
    private const string Director = "Kor.Operations.App/BusinessDevelopment/Director/";

    [Fact]
    public void Home_UsesDirectorRole_AndFailsClosed()
    {
        var home = Source("Kor.Operations.App/HomeWindow.xaml.cs");
        Contains(home, "SecurityGroupAccess.IsUserInGroup(KnownRoles.BdDirector, userIdentity)");
        Contains(home, "BdDirectorCard.Visibility = canSeeBdDirector ? Visibility.Visible : Visibility.Collapsed;");
        Contains(home, "BdDirectorCard.Visibility = state.BdDirector;");
        Assert.True(Regex.IsMatch(home, @"CooCardCard,\s+BdDirectorCard,"),
            "EXPECTED BD Director immediately after COO Card in Home order; ACTUAL order missing");
        Contains(home, "GetRequiredService<App.BusinessDevelopment.Director.BdDirectorWindow>()");
        Contains(Source("Kor.Operations.App/HomeWindow.xaml"), "Click=\"OpenBdDirector_Click\"");
        var state = global::Kor.Operations.HomeTileVisibilityState.ForSecurityLookupFailure();
        Assert.True(state.BdDirector == Visibility.Collapsed,
            $"EXPECTED failed lookup to collapse BD Director; ACTUAL {state.BdDirector}");
        Assert.True(KnownRoles.BdDirector == "BdDirector", $"EXPECTED role BdDirector; ACTUAL {KnownRoles.BdDirector}");
        var config = XDocument.Parse(Source("Kor.Operations.App/App.config"));
        var members = config.Descendants("add")
            .SingleOrDefault(e => (string?)e.Attribute("key") == "SecurityGroup.BdDirector.Members");
        var actual = (string?)members?.Attribute("value");
        Assert.True(actual == "ilalonde@korstructural.com", $"EXPECTED Ian's address in the Director group; ACTUAL {actual ?? "missing"}");
    }

    [Fact]
    public void Window_UsesPlainWords_AndBothExactCaptions()
    {
        const string people = "Everyone who owns a live pursuit. A live pursuit is Pending or Submitted. Counts are for that person's pursuits; opens are the BD screens they opened.";
        const string funnel = "Live pursuits by stage, how long they have sat there, and what has been decided. Deltek history is counted separately and never added to the live numbers.";
        var vm = Source(Director + "BdDirectorViewModel.cs");
        var xaml = Source(Director + "BdDirectorWindow.xaml");
        Contains(vm, people);
        Contains(vm, funnel);
        Contains(xaml, "{x:Static director:BdDirectorViewModel.PeopleCaption}");
        Contains(xaml, "{x:Static director:BdDirectorViewModel.FunnelCaption}");
        Assert.True(BdDirectorViewModel.PeopleCaption == people && BdDirectorViewModel.FunnelCaption == funnel,
            "EXPECTED both captions verbatim; ACTUAL caption constants differ");

        // Identifiers such as ICrmEngagementStore are not displayed wording.
        var literals = XDocument.Parse(xaml).Descendants().Attributes()
            .Where(a => new[] { "Text", "Content", "Header", "ToolTip", "Title" }.Contains(a.Name.LocalName))
            .Select(a => a.Value).Where(v => !v.StartsWith("{", StringComparison.Ordinal)).ToList();
        var code = Regex.Replace(vm, @"//[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
        literals.AddRange(Regex.Matches(code, "\"((?:\\\\.|[^\"\\\\])*)\"")
            .Cast<Match>().Select(m => m.Groups[1].Value));
        foreach (var literal in literals)
            Assert.False(Regex.IsMatch(literal, @"\b(Overwatch|Bazaar|engagement|CRM|canonical|UPN|staff id)\b", RegexOptions.IgnoreCase),
                $"EXPECTED plain pursuit/buyer/person wording; ACTUAL {literal}");
    }

    [Fact]
    public void Store_ReadsTheSpecifiedPopulations_AndGroupsByResolvedPerson()
    {
        var sql = Source("Kor.Opportunities.Data/Crm/SqlBdDirectorStore.cs");
        foreach (var clause in new[]
        {
            "e.Stage IN (1, 3)", "LEFT JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId",
            "GROUP BY COALESCE(s.Mailbox, e.OwnerStaffId)", "COALESCE(s.Mailbox, s.StaffKey)",
            "LEFT JOIN opportunities.CrmBuyerEmailWarmth w ON w.CanonicalOrgId = e.BuyerCanonicalOrgId",
            "FROM opportunities.BdUiOpens u", "k.PersonKey = p.PersonKey AND k.OwnerKey = u.ByStaffId",
            "Stage = 7 AND COALESCE(ExternalSource, N'') <> N'Deltek.CustomProposal'",
            "LostToCanonicalOrgId IS NOT NULL", "OutcomeReason IS NOT NULL",
            "PERCENTILE_CONT(0.5)", "h.EngagementId = e.Id AND h.Stage = e.Stage",
            "COALESCE(h.StageSinceUtc, e.OpenedAtUtc)", "o.SubmissionDeadlineUtc < DATEADD(day, -30, @now)",
            "e.Stage IN (1, 3) AND o.SubmissionDeadlineUtc IS NULL",
        }) Contains(sql, clause);
        Assert.False(Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|MERGE)\b", RegexOptions.IgnoreCase),
            "EXPECTED SELECT-only Director store; ACTUAL write keyword present");
    }

    [Fact]
    public void BriefStore_AddsExactlyTwoInsertsAndOneAcknowledgementUpdate()
    {
        var source = Source("Kor.Opportunities.Data/Crm/SqlDirectorBriefStore.cs");
        var writes = Regex.Matches(source, @"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM|MERGE)\s+(\w+\.\w+)", RegexOptions.IgnoreCase)
            .Cast<Match>().Select(m => Regex.Replace(m.Value, @"\s+", " ")).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "INSERT INTO opportunities.DirectorBrief", "INSERT INTO opportunities.DirectorBriefImport", "UPDATE opportunities.DirectorBrief" }, writes);
        Contains(source, "WHERE Id = @id AND AcknowledgedAtUtc IS NULL");
        Contains(source, "IsolationLevel.Serializable");
        Contains(source, "DirectorBriefImport WITH (UPDLOCK, HOLDLOCK) WHERE FileSha256 = @hash");
        Contains(source, "if (!force && existing is not null)");
        Contains(source, "knowledge.Claim c WITH (UPDLOCK, HOLDLOCK)");
        Contains(source, "c.RetiredAtUtc");
        Contains(source, "file.ValidateCurrentCitations(retiredAt)");
        Assert.True(source.IndexOf("if (!force && existing is not null)", StringComparison.Ordinal)
            < source.IndexOf("file.ValidateCurrentCitations(retiredAt)", StringComparison.Ordinal),
            "EXPECTED duplicate skip before retirement validation, with forced imports still validated.");
        Contains(source, "JsonSerializer.Serialize(section.Citations.Distinct().Select(id => citations[id])");
        Contains(source, "await tx.RollbackAsync(ct)");
        Contains(source, "await tx.CommitAsync(ct)");
        Contains(source, "PARTITION BY Section ORDER BY ImportedAtUtc DESC, Id DESC");
        Contains(source, "WeekOf = (SELECT MAX(WeekOf) FROM opportunities.DirectorBrief)");
        Contains(source, "SELECT DISTINCT TOP (@weeks) WeekOf");
        Contains(Source("Kor.Operations.App/CompositionModules/OpportunitiesModule.cs"),
            "services.AddSingleton<IDirectorBriefStore>(_ => new SqlDirectorBriefStore(options.OpportunitiesDb))");
    }

    [Fact]
    public void Migration352_IsGuardedAdditiveDdl_TwoTablesAndOneIndex()
    {
        var sql = Source("Kor.Opportunities.Data/Schema/352_DirectorBrief.sql");
        var statements = Regex.Replace(sql, @"/\*.*?\*/|--[^\r\n]*", "", RegexOptions.Singleline);
        Assert.False(Regex.IsMatch(statements, @"\b(USE|DROP|ALTER|INSERT|UPDATE|DELETE|MERGE|GRANT)\b", RegexOptions.IgnoreCase));
        Assert.True(Regex.Matches(statements, @"CREATE TABLE\b").Count == 2, "EXPECTED two guarded tables.");
        Assert.True(Regex.Matches(statements, @"CREATE INDEX\b").Count == 1, "EXPECTED one guarded index.");
        foreach (var table in new[] { "DirectorBrief", "DirectorBriefImport" })
            Contains(sql, $"IF OBJECT_ID(N'opportunities.{table}', N'U') IS NULL");
        Contains(sql, "IF NOT EXISTS (SELECT 1 FROM sys.indexes");
        Contains(sql, "IX_Opportunities_DirectorBrief_WeekSection ON opportunities.DirectorBrief (WeekOf DESC, Section)");
        Contains(sql, "CHECK (Section IN (N'ThisWeek', N'Pursuits', N'Accounts', N'Market', N'Asks'))");
        foreach (var column in new[] { "WeekOf DATE NOT NULL", "Headline NVARCHAR(512) NOT NULL", "Body NVARCHAR(MAX) NOT NULL",
                     "Recommendation NVARCHAR(MAX) NULL", "Citations NVARCHAR(MAX) NOT NULL", "DEFAULT N'[]'", "WrittenBy NVARCHAR(100) NOT NULL",
                     "ImportedAtUtc DATETIMEOFFSET NOT NULL", "ImportedBy NVARCHAR(100) NOT NULL", "AcknowledgedAtUtc DATETIMEOFFSET NULL",
                     "AcknowledgedBy NVARCHAR(100) NULL", "FileName NVARCHAR(260) NOT NULL", "FileSha256 CHAR(64) NOT NULL",
                     "ExportSha256 CHAR(64) NOT NULL", "SectionsWritten INT NOT NULL", "Forced BIT NOT NULL" }) Contains(sql, column);
        Assert.True(Regex.Matches(sql, @"^GO\s*$", RegexOptions.Multiline).Count == 4, "EXPECTED four guarded/setup batches.");
    }

    [Fact]
    public void PendingBidsSql_IsReadOnly_RequiresRfp_ResolvesOwner_AndReadsLatestSubmissionHistory()
    {
        var store = Source("Kor.Opportunities.Data/Crm/SqlBdDirectorStore.cs");
        var sql = Regex.Match(store, "PendingBidsSql = @\"(.*?)\";", RegexOptions.Singleline).Groups[1].Value;
        Assert.NotEmpty(sql);
        Assert.False(Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|MERGE|EXEC|INTO)\b", RegexOptions.IgnoreCase));
        foreach (var clause in new[]
        {
            "e.OpportunityId IS NOT NULL AND e.Stage IN (1, 3)",
            "JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId",
            "LEFT JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId",
            "o.OpportunityKey, o.Name, o.BuyerName", "o.SubmissionDeadlineUtc AS DueUtc",
            "s.DisplayName, s.Mailbox", "FROM opportunities.CrmEngagementStageHistory h",
            "MAX(h.EnteredAtUtc) AS SubmittedAtUtc", "h.EngagementId = e.Id AND h.Stage = 3",
            "ORDER BY CASE WHEN o.SubmissionDeadlineUtc IS NULL THEN 1 ELSE 0 END",
            "o.SubmissionDeadlineUtc, e.Id", "e.SubmittedFileName, e.FolderLink, e.NextActionDueUtc",
        }) Contains(sql, clause);
        var count = Regex.Match(store, "RelationshipPursuitsSql = @\"(.*?)\";", RegexOptions.Singleline).Groups[1].Value;
        Contains(count, "OpportunityId IS NULL AND Stage IN (1, 3)");
        Assert.False(Regex.IsMatch(count, @"\b(INSERT|UPDATE|DELETE|MERGE|EXEC|INTO)\b", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void Migration353_IsExactlyTwoGuardedColumnAdds_InSeparateBatches()
    {
        var source = Source("Kor.Opportunities.Data/Schema/353_PursuitSubmission.sql");
        var sql = Regex.Replace(source, @"/\*.*?\*/|--[^\r\n]*", "", RegexOptions.Singleline);
        var batches = Regex.Split(sql, @"^GO\s*$", RegexOptions.Multiline).Where(b => !string.IsNullOrWhiteSpace(b)).ToArray();
        Assert.Equal(2, batches.Length);
        foreach (var pair in new[] { ("SubmittedFileName", 260), ("FolderLink", 1000) }.Select((value, index) => (value, index)))
        {
            var (column, size) = pair.value;
            Assert.Equal($"IF COL_LENGTH(N'opportunities.CrmEngagements', N'{column}') IS NULL ALTER TABLE opportunities.CrmEngagements ADD {column} NVARCHAR({size}) NULL;",
                Regex.Replace(batches[pair.index], @"\s+", " ").Trim());
        }
        Assert.False(Regex.IsMatch(sql, @"\b(CREATE|DROP|INSERT|UPDATE|DELETE|MERGE|GRANT|USE)\b", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void EngagementWriteBoundary_AddsExactlyTheTwoSubmissionColumns_PreservesHistoryAndConcurrency()
    {
        var source = Source("Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs");
        var insert = Regex.Match(source, @"INSERT INTO opportunities\.CrmEngagements\s*\((.*?)\)\s*OUTPUT", RegexOptions.Singleline);
        Assert.True(insert.Success);
        var expectedInsert = new[] { "OpportunityId", "Stage", "OwnerStaffId", "AssignedStaffIds", "TargetMargin", "ProposedFee", "ProposedHours", "Notes",
            "OpenedAtUtc", "ClosedAtUtc", "OutcomeNotes", "BuyerCanonicalOrgId", "Region", "ProposalsSubmittedCad", "ProposalsAcceptedCad", "PotentialProjects",
            "WonLostOutcome", "OutcomeReason", "LostToCanonicalOrgId", "WonProjectWbs1", "NextActionDueUtc", "NextActionNote", "ExternalSource", "ExternalSourceKey",
            "CreatedBy", "UpdatedBy", "SubmittedFileName", "FolderLink" };
        Assert.Equal(expectedInsert.OrderBy(c => c), insert.Groups[1].Value.Split(',').Select(c => c.Trim()).OrderBy(c => c));
        var update = Regex.Match(source, @"UPDATE opportunities\.CrmEngagements\s+SET\s+(.*?)\s+OUTPUT", RegexOptions.Singleline);
        Assert.True(update.Success);
        var actualUpdate = Regex.Matches(update.Groups[1].Value, @"(?:^|,)\s*(\w+)\s*=").Select(m => m.Groups[1].Value).OrderBy(c => c);
        var expectedUpdate = expectedInsert.Except(new[] { "OpportunityId", "ExternalSource", "ExternalSourceKey", "CreatedBy" }).Append("UpdatedAtUtc");
        Assert.Equal(expectedUpdate.OrderBy(c => c), actualUpdate);
        foreach (var column in new[] { "SubmittedFileName", "FolderLink" })
            Assert.Equal(2, Regex.Matches(source, "inserted\\." + column + @"\b").Count);
        foreach (var clause in new[] { "SubmittedFileName = r.IsDBNull(30) ? null : r.GetString(30)",
                     "FolderLink = r.IsDBNull(31) ? null : r.GetString(31)", "previousStage = reader.GetInt32(32)",
                     "SqlDbType.NVarChar, 260", "SqlDbType.NVarChar, 1000", "WHERE Id = @id AND RowVersion = @rv",
                     "WriteStageHistoryAsync(con, tx, updated.Id, (int)updated.Stage, actorDisplay, ct)",
                     "OpportunityStatusFor(updated.Stage)", "(int)status", "await tx.CommitAsync(ct)",
                     "ClosedAtUtc           = CASE WHEN @stage IN (6, 7)", "ELSE NULL END" }) Contains(source, clause);
        var writes = Regex.Matches(source, @"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM|MERGE)\s+(\w+\.\w+)", RegexOptions.IgnoreCase)
            .Select(m => Regex.Replace(m.Value, @"\s+", " ")).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "INSERT INTO opportunities.CrmEngagements", "INSERT INTO opportunities.CrmEngagementStageHistory",
            "UPDATE opportunities.CrmEngagements", "UPDATE opportunities.Opportunities" }, writes);
    }

    [Theory]
    [InlineData(CrmEngagementStage.Drafting, OpportunityStatus.Pursuing)]
    [InlineData(CrmEngagementStage.Submitted, OpportunityStatus.Submitted)]
    [InlineData(CrmEngagementStage.Won, OpportunityStatus.Won)]
    [InlineData(CrmEngagementStage.Lost, OpportunityStatus.Lost)]
    public void EngagementStage_MapsToTheParentStatus_NotTheStageInteger(CrmEngagementStage stage, OpportunityStatus expected)
    {
        var method = typeof(SqlCrmEngagementStore).GetMethod("OpportunityStatusFor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(expected, Assert.IsType<OpportunityStatus>(method.Invoke(null, new object[] { stage })));
        Assert.NotEqual((int)CrmEngagementStage.Submitted, (int)OpportunityStatus.Submitted);
    }

    [Fact]
    public void OpportunityTooltip_HasNoOldScreenName()
    {
        var xaml = Source("Kor.Operations.App/BusinessDevelopment/Workspace/OpportunitiesHubView.xaml");
        Assert.False(xaml.Contains("Bazaar", StringComparison.OrdinalIgnoreCase),
            "EXPECTED no Bazaar in opportunity hub XAML; ACTUAL old name remains");
        Contains(xaml, "Available, unclaimed opportunities you can take on.");
    }

    [Fact]
    public void Director_IsInsideDateRatchet_AndContributesZero()
    {
        var ratchet = Source("Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdPresentationRegressionTests.cs");
        Contains(ratchet, "\"BusinessDevelopment/Director\"");
        Contains(ratchet, "total <= 64");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), Director)))
        {
            if (Path.GetExtension(file) is not ".cs" and not ".xaml") continue;
            var count = Regex.Matches(File.ReadAllText(file), "yyyy-MM-dd|MMM d, yyyy").Count;
            Assert.True(count == 0, $"EXPECTED zero inline date formats in {Path.GetFileName(file)}; ACTUAL {count}");
        }
        Contains(Source(Director + "BdDirectorWindow.xaml"), "<converters:BdDayConverter x:Key=\"BdDay\"/>");
    }

    [Fact]
    public async Task Drill_IncludesAllOwnerSpellings_OnlyInLiveStages()
    {
        var fixture = new Fixture();
        fixture.People.Add(Person());
        fixture.Rows.AddRange(new[] { Pursuit(1, "Omar", 1), Pursuit(2, "OMARA@korstructural.com", 3),
            Pursuit(3, "Omar", 6), Pursuit(4, "someone else", 1) });
        var vm = fixture.Create();
        await vm.LoadAsync(CancellationToken.None);
        var ids = vm.SelectedPursuits.Select(p => p.PursuitId).ToArray();
        Assert.True(ids.SequenceEqual(new long[] { 1, 2 }),
            $"EXPECTED alias drill pursuits 1,2; ACTUAL {string.Join(",", ids)}");
        Assert.True(vm.People.Count == 1, $"EXPECTED one already-grouped person; ACTUAL {vm.People.Count}");
    }

    [Fact]
    public async Task Funnel_UsesAnalyticsSegmentation_AndScorecardSourceRows()
    {
        var fixture = new Fixture();
        fixture.Records.AddRange(new[]
        {
            new CrmEngagement { Stage = CrmEngagementStage.Drafting },
            new CrmEngagement { Stage = CrmEngagementStage.Submitted },
            new CrmEngagement { Stage = CrmEngagementStage.Won },
            new CrmEngagement { Stage = CrmEngagementStage.Lost },
            new CrmEngagement { Stage = CrmEngagementStage.Won, ExternalSource = "Deltek.CustomProposal" },
        });
        var source = new AttributionSourceRow("Deltek.CustomProposal", 1, 5000m);
        fixture.Attribution.Setup(s => s.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BdAttributionSummary(1, 1000m, 2, 1, 1, new[] { source }, Array.Empty<AttributionOwnerRow>()));
        var vm = fixture.Create();
        await vm.LoadAsync(CancellationToken.None);
        Assert.True(vm.Live == 2 && vm.Stages.Sum(s => s.Pursuits) == 2,
            $"EXPECTED two live pursuits; ACTUAL {vm.Live}");
        var expected = $"Won 1 · Lost 1 · win rate {0.5.ToString("P0", CultureInfo.CurrentCulture)} on decided live pursuits · 1 Deltek wins kept separate";
        Assert.True(vm.Outcomes == expected, $"EXPECTED {expected}; ACTUAL {vm.Outcomes}");
        Assert.True(vm.Sources.Count == 1 && ReferenceEquals(source, vm.Sources[0]),
            "EXPECTED unchanged Scorecard source row; ACTUAL row missing or recomputed");
        fixture.Records.RemoveAll(e => e.Stage is CrmEngagementStage.Won or CrmEngagementStage.Lost);
        await vm.LoadAsync(CancellationToken.None);
        Assert.True(vm.Outcomes == "No live pursuit has been decided yet.",
            $"EXPECTED no-decisions sentence; ACTUAL {vm.Outcomes}");
    }

    [Fact]
    public async Task Reassign_PassesStoredOwner_AndReloadsBothPanes()
    {
        var fixture = new Fixture();
        fixture.People.Add(Person());
        fixture.Rows.Add(Pursuit(1, "omara@korstructural.com", 1));
        fixture.Pursuits.Setup(s => s.ReassignAsync(1, null, "omara@korstructural.com", "new owner", "actor", "reason", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReassignOutcome.Reassigned);
        var vm = fixture.Create();
        await vm.LoadAsync(CancellationToken.None);
        var row = Assert.Single(vm.SelectedPursuits);
        var outcome = await vm.ReassignAsync(row, "new owner", "actor", "reason", CancellationToken.None);
        Assert.True(outcome == ReassignOutcome.Reassigned, $"EXPECTED Reassigned; ACTUAL {outcome}");
        fixture.Pursuits.Verify(s => s.ReassignAsync(1, null, "omara@korstructural.com", "new owner", "actor", "reason", It.IsAny<CancellationToken>()), Times.Once);
        fixture.DirectorStore.Verify(s => s.GetPeopleAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.DirectorStore.Verify(s => s.GetFunnelAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Attribution.Verify(s => s.GetSummaryAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Pursuits.Verify(s => s.ListAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(ReassignOutcome.OwnerChanged, "“Example” was already moved by someone else — refresh to see who owns it.")]
    [InlineData(ReassignOutcome.DuplicateForTarget, "new owner already holds a pursuit for this buyer/region — not moved.")]
    [InlineData(ReassignOutcome.PursuitClosed, "“Example” has closed (Won/Lost) since the board loaded — not moved. Refresh the board.")]
    public async Task Reassign_RefusalsUseExistingSentences(ReassignOutcome result, string expected)
    {
        var fixture = new Fixture();
        fixture.Pursuits.Setup(s => s.ReassignAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        var vm = fixture.Create();
        var actual = await vm.ReassignAsync(new BdDirectorPursuitRow(Pursuit(1, "Omar", 1)), "new owner", "actor", null, CancellationToken.None);
        Assert.True(actual == result && vm.StatusMessage == expected,
            $"EXPECTED {result}: {expected}; ACTUAL {actual}: {vm.StatusMessage}");
        fixture.DirectorStore.Verify(s => s.GetPeopleAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reassign_InFlightClickIsIgnored_WithoutClaimingAnOwnerChange()
    {
        var fixture = new Fixture();
        var pending = new TaskCompletionSource<ReassignOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Pursuits.Setup(s => s.ReassignAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(pending.Task);
        var vm = fixture.Create();
        var row = new BdDirectorPursuitRow(Pursuit(1, "Omar", 1));
        var first = vm.ReassignAsync(row, "new owner", "actor", null, CancellationToken.None);
        var status = vm.StatusMessage;
        var second = await vm.ReassignAsync(row, "other owner", "actor", null, CancellationToken.None);
        Assert.True(second == ReassignOutcome.Ignored && vm.StatusMessage == status,
            $"EXPECTED Ignored with unchanged message {status}; ACTUAL {second}: {vm.StatusMessage}");
        pending.SetResult(ReassignOutcome.OwnerChanged);
        await first;
        fixture.Pursuits.Verify(s => s.ReassignAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static BdDirectorPerson Person() => new("omara@korstructural.com", "Omar Alcazar Pastrana",
        new[] { "Omar", "omara@korstructural.com" }, 2, 0, 2, 2, 0, 0, 0, 0, 0);

    private static PursuitOverwatchRow Pursuit(long id, string owner, int stage) => new(id, null, owner,
        stage, "Example", "Buyer", null, null, DateTimeOffset.UtcNow, 0, null, DateTimeOffset.UtcNow);

    private sealed class Fixture
    {
        internal readonly Mock<IBdDirectorStore> DirectorStore = new();
        internal readonly Mock<IPursuitOverwatchStore> Pursuits = new();
        internal readonly Mock<ICrmEngagementStore> RecordStore = new();
        internal readonly Mock<IBdAttributionStore> Attribution = new();
        internal readonly Mock<IBdDirectorLedgerStore> Ledger = new();
        internal readonly Mock<IDeltekClientContextService> Deltek = new();
        internal readonly Mock<IKnowledgeClaimStore> Claims = new();
        internal readonly List<BdDirectorPerson> People = new();
        internal readonly List<PursuitOverwatchRow> Rows = new();
        internal readonly List<CrmEngagement> Records = new();

        internal Fixture()
        {
            DirectorMarketDefaults.SetupPendingBids(DirectorStore);
            Claims.Setup(s => s.ListSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<KnowledgeSourceSummary>());
            Claims.Setup(s => s.ListConceptsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<KnowledgeConceptRow>());
            Ledger.Setup(s => s.ListTargetsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdTarget>());
            Ledger.Setup(s => s.ListAsksAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdAsk>());
            Ledger.Setup(s => s.ListDecisionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDecision>());
            DirectorStore.Setup(s => s.GetTargetActualsAsync(It.IsAny<IReadOnlyList<BdTargetKey>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BdTargetActuals(new Dictionary<BdTargetKey, decimal>(), 0, 0));
            DirectorStore.Setup(s => s.GetAccountsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDirectorAccount>());
            DirectorStore.Setup(s => s.GetRecentSignalsAsync(30, 25, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDirectorSignal>());
            DirectorStore.Setup(s => s.GetPeopleAsync(It.IsAny<CancellationToken>())).ReturnsAsync(People);
            DirectorStore.Setup(s => s.GetFunnelAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BdDirectorFunnel(new Dictionary<int, double>(), Array.Empty<BdDirectorUndecidedPursuit>(), 0, 0, 0, 0));
            Pursuits.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Rows);
            RecordStore.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Records);
            Attribution.Setup(s => s.GetSummaryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BdAttributionSummary(0, 0m, 0, 0, 0, Array.Empty<AttributionSourceRow>(), Array.Empty<AttributionOwnerRow>()));
        }

        internal BdDirectorViewModel Create() => new(DirectorStore.Object, Pursuits.Object, RecordStore.Object, Attribution.Object, Ledger.Object, Deltek.Object, Claims.Object,
            DirectorMarketDefaults.Dashboard(), DirectorMarketDefaults.Opportunities(), DirectorMarketDefaults.Brief());
    }

    private static void Contains(string source, string expected) => Assert.True(source.Contains(expected, StringComparison.Ordinal),
        $"EXPECTED source containing {expected}; ACTUAL missing");

    private static string Source(string path) => File.ReadAllText(Path.Combine(RepoRoot(), path));

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("EXPECTED repository root; ACTUAL not found");
    }
}
