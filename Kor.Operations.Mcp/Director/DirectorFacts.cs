using System.Globalization;
using System.Text;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.MajorProjects;

namespace Kor.Operations.Mcp.Director;

/// <summary>One facts load per section. Each needed store method is read once in that load.
/// The compact blocks are bounded; missing data is identified instead of inferred.</summary>
public sealed class DirectorFacts(IBdDirectorStore director, IBdDirectorLedgerStore ledger, IBdDashboardStore dashboard)
{
    public async Task<string> GetAsync(DirectorSection section, CancellationToken ct)
    {
        var block = new Block();
        block.Add("As of UTC", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        switch (section)
        {
            case DirectorSection.ThisWeek:
                var targets = await ledger.ListTargetsAsync(ct);
                var keys = targets.Select(t => new BdTargetKey(t.Metric, t.Scope, t.ScopeKey, t.PeriodStartUtc, t.PeriodEndUtc)).ToArray();
                var actuals = await director.GetTargetActualsAsync(keys, ct);
                block.Add("Live pursuits", actuals.Live, "with fee", actuals.LiveWithFee);
                block.Add("Target | scope | scope name | period start/end UTC | value | actual");
                for (var i = 0; i < targets.Count; i++) block.Add(targets[i].Metric, targets[i].Scope, targets[i].ScopeKey,
                    targets[i].PeriodStartUtc, targets[i].PeriodEndUtc, targets[i].TargetValue,
                    actuals.Actuals.TryGetValue(keys[i], out var actual) ? actual : (decimal?)null);
                AddPeople(block, await director.GetPeopleAsync(ct), 12);
                AddAccounts(block, await director.GetAccountsAsync(12, ct));
                break;
            case DirectorSection.Pursuits:
                AddPeople(block, await director.GetPeopleAsync(ct), 20);
                var funnel = await director.GetFunnelAsync(ct);
                block.Add("Stage | median days");
                foreach (var age in funnel.MedianDaysInStage) block.Add(age.Key, age.Value);
                block.Add("Undecided without deadline", funnel.UndecidedNoDeadline, "lost", funnel.LostTotal, "named competitor", funnel.LostWithCompetitor, "reason recorded", funnel.LostWithReason);
                block.Add("Overdue undecided pursuit | buyer | owner | deadline UTC");
                foreach (var p in funnel.UndecidedPastDeadline) block.Add(p.EngagementId, p.ProjectName, p.Buyer, p.OwnerKey, p.Deadline);
                block.Add("Recent decisions | expected | outcome");
                foreach (var d in (await ledger.ListDecisionsAsync(ct)).Take(10)) block.Add(d.Decision, d.ExpectedOutcome, d.Outcome);
                block.Add("Movement counts cover 90 days; no week-over-week history is supplied.");
                break;
            case DirectorSection.Accounts:
                AddAccounts(block, await director.GetAccountsAsync(40, ct));
                break;
            case DirectorSection.Market:
                var pipeline = await dashboard.GetPipelineFunnelAsync(ct);
                block.Add("Radar | bid window | open seats", pipeline.RadarCount, pipeline.BidWindowCount, pipeline.OpenSeatCount);
                block.Add("Firm | signal | subject | last seen UTC | corroborations");
                foreach (var s in await director.GetRecentSignalsAsync(30, 25, ct)) block.Add(s.Org, s.Signal, s.Subject, s.LastSeenAtUtc, s.Corroborations);
                block.Add("Forward project | proponent | province | sector | estimated cost CAD | stage");
                foreach (var p in await dashboard.GetForwardPipelineAsync(12, ct)) block.Add(p.ProjectName, p.ProponentName, p.Province, p.Sector, p.EstimatedCostCad, p.Stage);
                block.Add("RFP deadlines are not included in this facts block; do not invent them.");
                break;
            case DirectorSection.Asks:
                block.Add("Open ask | to | minutes | due UTC | about pursuit/account | why");
                foreach (var ask in (await ledger.ListAsksAsync(null, ct)).Where(a => a.Status == "Open"))
                    block.Add(ask.Ask, ask.ToStaffKey, ask.Minutes, ask.DueUtc, ask.EngagementId, ask.CanonicalOrgId, ask.WhyNow);
                block.Add("Assignment suitability cannot be established without availability or role evidence.");
                break;
            default: throw new ArgumentOutOfRangeException(nameof(section));
        }
        return block.ToString();
    }

    private static void AddPeople(Block block, IReadOnlyList<BdDirectorPerson> people, int take)
    {
        block.Add("Person | live | overdue next action | missing next action | untouched 14d | activity 30d | stage moves 90d");
        foreach (var p in people.Take(take)) block.Add(p.DisplayName, p.Live, p.OverdueNextAction, p.NoNextAction, p.Untouched14d, p.Activities30, p.StageMoves90);
    }
    private static void AddAccounts(Block block, IReadOnlyList<BdDirectorAccount> accounts)
    {
        block.Add("Client | project records (includes proposals) | live pursuits | last touch UTC | open asks");
        foreach (var a in accounts) block.Add(a.DisplayName, a.KorProjectsCount, a.LivePursuits, a.LastTouchUtc, a.OpenAsks);
    }
    private sealed class Block
    {
        private readonly StringBuilder _text = new();
        private bool _full;
        public void Add(params object?[] cells)
        {
            if (_full) return;
            var line = string.Join(" | ", cells.Select(c => (c switch
            {
                null => "not recorded",
                DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
                IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
                _ => c.ToString() ?? "",
            }).Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/')));
            if (line.Length > 700) line = line[..697] + "...";
            if (_text.Length + line.Length + 1 > 5930) { _text.AppendLine("Additional rows omitted to keep this facts block bounded."); _full = true; return; }
            _text.Append(line).Append('\n');
        }
        public override string ToString() => _text.ToString();
    }
}
