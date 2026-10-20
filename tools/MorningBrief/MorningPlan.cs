using System.Globalization;
using Kor.Opportunities.Data.Plan;

namespace Kor.MorningBrief;

/// <summary>
/// The operating plan's goals as the morning session reads them (migration 372): one line per live goal with its stage, its
/// reading now, its baseline and target, and whether Ian has decided it. The prompt requires every move to name the goals it
/// advances by these ids, and the import refuses a move that names none. Pure; the readings are the Plan tab's own.
/// </summary>
public static class MorningPlan
{
    public static MorningFact Fact(OperatingPlanModel plan, PlanReadings readings)
    {
        var stages = plan.Stages.ToDictionary(s => s.No, s => s.Name);
        var lines = plan.Goals.Select(g =>
            $"goal {g.Id.ToString(CultureInfo.InvariantCulture)} · {g.Pillar} · stage {(stages.TryGetValue(g.StageNo, out var n) ? n : g.StageNo.ToString(CultureInfo.InvariantCulture))}: "
            + $"{g.Title} — now {OperatingPlanHtml.Now(g, readings)}, baseline {OperatingPlanHtml.Baseline(g, readings)}, "
            + $"target {OperatingPlanHtml.Target(g, readings)} by {g.DueDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}; {g.Status.ToLowerInvariant()}.").ToArray();
        return new MorningFact($"Operating plan goals ({lines.Length}; every move names the goals it advances, by id)", lines, lines.Length, lines.Length);
    }
}
