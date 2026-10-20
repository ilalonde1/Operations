#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;

namespace Kor.Opportunities.Data.Plan;

/// <summary>
/// Gathers the plan, the day's readings and today's detail from the stores that already hold them, and draws the page. The
/// Director's Plan tab and its Print plan button both call this, so the screen and the PDF are one page. Nothing here is
/// computed twice: pursuit counts and roll-up asks come from the touchstone store and calculation the roll-ups use, money and
/// project figures from the scoreboard's daily readings, today's COO and IT work from the imported morning hour.
/// </summary>
public static class OperatingPlanPage
{
    /// <summary>The scoreboard's first day (MorningImpact.Baseline in the composer): every daily metric's baseline.</summary>
    public static readonly DateOnly ScoreboardBaseline = new(2026, 10, 18);

    public static async Task<string> BuildAsync(IOperatingPlanStore plan, IPursuitTouchstoneStore touchstones,
        Func<DateOnly, CancellationToken, Task<IReadOnlyList<ImpactReading>>> scoreboard, IBdRhythmStore rhythm,
        DateTimeOffset now, CancellationToken ct, bool summaryOnly = false)
    {
        var zone = MeetingsDue.Pacific();
        var model = await plan.LoadAsync(ct).ConfigureAwait(false);
        var radar = await plan.ReadRadarAsync(ct).ConfigureAwait(false);
        var pursuits = await touchstones.ListPursuitsAsync(30, ct).ConfigureAwait(false);
        var drafts = await touchstones.ListDraftsAsync(ct).ConfigureAwait(false);
        var due = PursuitTouchstones.Compute(await touchstones.ListRulesAsync(ct).ConfigureAwait(false), pursuits, drafts, now, zone);
        var asks = PursuitTouchstones.Rollups(due, pursuits, drafts, now, zone).SelectMany(r => r.Due).ToList();
        var readings = await scoreboard(ScoreboardBaseline, ct).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).Date);
        var hourRow = await rhythm.GetHourAsync("Morning", today, ct).ConfigureAwait(false);
        var hour = hourRow is null ? null : DirectorHourFile.Parse(hourRow.ContentJson).Content;
        // The worked example is the pursuit the plan names in its "Example" point, read live.
        var exampleId = model.Points.FirstOrDefault(p => p.Section == "Example")?.Detail;
        var example = long.TryParse(exampleId, out var id) && id > 0 ? await plan.ReadExampleAsync(id, ct).ConfigureAwait(false) : null;
        return Draw(model, readings, pursuits, radar, asks, hour, now, summaryOnly, example);
    }

    /// <summary>The pure half: everything gathered, the page drawn. Tests and the render harness call this directly.</summary>
    public static string Draw(OperatingPlanModel model, IReadOnlyList<ImpactReading> scoreboard, IReadOnlyList<PursuitState> pursuits,
        PlanRadar? radar, IReadOnlyList<TouchstoneDue> asks, DirectorHourContent? hour, DateTimeOffset now, bool summaryOnly = false,
        PlanExample? example = null)
    {
        var readings = PlanReadings.From(scoreboard, pursuits, radar, asks.Count, now);
        return OperatingPlanHtml.Render(model, readings, PlanItems.From(asks, hour, model.Goals.ToDictionary(g => g.Id, g => g.StageNo)), now,
            summaryOnly, example);
    }
}
