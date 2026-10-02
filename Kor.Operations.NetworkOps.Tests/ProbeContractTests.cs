#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Probes;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The class of fault found twice on 2026-09-29, as one check: AN EVENT COUNT THAT DOES NOT PIN WHAT IT
// COUNTS COUNTS THINGS THAT ARE NOT THE FAULT IT NAMES.
//   - WHEA counted a whole provider, so Information-level notices read as "hardware errors" (206-N: 5 of 5).
//   - GPU hangs counted WER log entries, and Windows re-logs one report on every send retry (~100x).
//
// WHAT IT COVERS: every Count-Events call in the health probe names specific event ids or a severity
// level (never a whole provider); the GPU count is de-duplicated by report AND dated by the report's folder
// (v8); no WER-derived variable other than the raw-entry tally reaches Summ; WER entries are read by field,
// never by rendering .Message; memory-layout on 206-N's real module set and on balanced/single-stick edges.
// The v8 dating was proven live on 7 of the 8 flagged PCs (2026-10-01): entry counts equal to v7's on all 7
// (the field positions are right), real resets 9 -> 5 on 104N and 0 on the other six.
// WHAT IT DOES NOT: whether a pinned id means what its rule says -- only a person reading real events
// on a real machine proves that (how both faults were actually found). A SAME-CLASS FAULT IT WOULD NOT
// CATCH: a pinned id that Windows ALSO logs repeatedly per incident (like WER 1001) passes this check;
// only comparing distinct incidents against raw entries on a live PC shows it.
public sealed class ProbeContractTests
{
    private static readonly string Probe = ProbeLibrary.Get(ProbeLibrary.Health);

    [Fact]
    public void Every_event_count_pins_ids_or_a_level_never_a_whole_provider()
    {
        var calls = Regex.Matches(Probe, @"Count-Events\s+'[^']+'\s+@\{([^}]*)\}").Select(m => m.Groups[1].Value).ToList();
        Assert.True(calls.Count >= 10, $"found only {calls.Count} Count-Events calls: the pattern no longer matches the probe");
        var loose = calls.Where(c => !Regex.IsMatch(c, @"\bId\s*=") && !Regex.IsMatch(c, @"\bLevel\s*=")).ToList();
        Assert.True(loose.Count == 0, "event counts that name a whole provider (add Id = or Level =):\n" + string.Join("\n", loose));
    }

    // The THIRD instance, 2026-10-01, is the one the header below predicted: WER re-logs a report on every retry to send
    // it, for months, so even one-per-report counted a 2025 report as a 2026 reset (6 of 7 flagged PCs had 0 real resets
    // in 14 d). The class, in one sentence: A WER-RE-LOGGED INCIDENT MUST BE DATED BY ITS REPORT, NOT BY ANY LOG ENTRY.
    [Fact]
    public void Gpu_resets_are_counted_once_per_report_and_dated_by_the_report_folder()
    {
        Assert.Matches(new Regex(@"Group-Object\s*\{\s*""\$\(\$_\.Properties\[19\]\.Value\)""\s*\}"), Probe);   // one per Report Id
        Assert.Matches(new Regex(@"TimeCreated\s*=\s*if\s*\(\$folder\)\s*\{\s*\$folder\.CreationTime\s*\}\s*else\s*\{\s*\$null\s*\}"), Probe);
        Assert.Matches(new Regex(@"\$gpuNew\s*=.*TimeCreated\s+-ge\s+\$since14"), Probe);
        Assert.Matches(new Regex(@"GpuHang\s*=\s*Summ\s+\$gpuNew\b"), Probe);
    }

    private static HealthSnapshot Fixture(string name) => HealthSnapshot.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "health", name)));

    [Fact]
    public void Kor217_five_queued_reports_from_2025_and_June_are_not_hangs()
    {
        var s = Fixture("KOR-217-v8-2026-10-01.json");
        Assert.Equal(8, s.ProbeVersion);
        Assert.Equal(0, s.Events14d!.GpuHang!.Count);
        Assert.Equal(5, s.Events14d.GpuHangStaleReports);
        Assert.DoesNotContain(HealthRules.Evaluate(s), f => f.RuleKey == "gpu-hangs");
    }

    [Fact]
    public void Kor104N_counts_only_the_resets_created_in_the_window_and_says_so()
    {
        var s = Fixture("KOR-104N-v8-2026-10-01.json");
        var f = Assert.Single(HealthRules.Evaluate(s), x => x.RuleKey == "gpu-hangs");
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("5 GPU resets in 14 d, newest 2026-09-29 17:49", f.Evidence);
        Assert.Contains("48 older reports still queued, not counted", f.Evidence);
        Assert.Equal(12, s.Events14d!.GpuHangReports!.Count);   // the evidence a session reads first
    }

    [Fact]
    public void No_count_built_on_WER_entries_is_summarised_straight_from_the_log()
    {
        // Every variable filled from the WER provider may only reach Summ through the dated report list.
        var werVars = Regex.Matches(Probe, @"\$(\w+)\s*=\s*(?:Count-Events\s+'Application'\s+@\{\s*ProviderName\s*=\s*'Windows Error Reporting'|@\(\$wer\b)")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Contains("wer", werVars);
        var straight = werVars.Where(v => Regex.IsMatch(Probe, $@"Summ\s+\(?\${v}\b") && v != "gpuWer").ToList();
        Assert.True(straight.Count == 0, "WER entries summarised as incidents: " + string.Join(", ", straight));
    }

    [Fact]
    public void Wer_entries_are_read_by_field_not_by_rendering_their_message()
        // Rendering .Message is the slow part of an event read (a full-log render timed out at 600 s on KOR-217).
        => Assert.DoesNotMatch(new Regex(@"\$wer\b[^\n]*\.Message"), Probe);

    private static HealthSnapshot WithMemory(int? slots, params MemoryModuleInfo[] mods) => new() { Memory = mods, MemorySlots = slots, ProbeVersion = 3 };
    private static MemoryModuleInfo M(string slot, int gb, int rated = 4800, int running = 4800) => new(slot, null, gb, rated, running, "Kingston", "x");

    [Fact]
    public void Kor206N_three_modules_unbalanced_and_slowed_is_one_info_finding()
    {
        var s = WithMemory(4, M("Controller0-ChannelA-DIMM1", 32, 4800, 3600), M("Controller1-ChannelB-DIMM0", 32, 4800, 3600), M("Controller1-ChannelB-DIMM1", 32, 4800, 3600));
        var f = HealthRules.Evaluate(s).Single(x => x.RuleKey == "memory-layout");
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Contains("3 x 32 GB = 96 GB in 3 of 4 slots", f.Evidence);
        Assert.Contains("32 GB runs single-channel", f.Evidence);
        Assert.Contains("rated 4800, running 3600", f.Evidence);
    }

    [Fact]
    public void Two_matched_modules_at_rated_speed_is_fine()
        => Assert.Null(HealthRules.MemoryLayout(WithMemory(4, M("Controller0-ChannelA-DIMM1", 32), M("Controller1-ChannelB-DIMM1", 32))));

    [Fact]
    public void One_module_in_a_four_slot_board_runs_single_channel()
        => Assert.Contains("single-channel", HealthRules.MemoryLayout(WithMemory(4, M("Controller0-ChannelA-DIMM1", 16))));

    [Fact]
    public void Unnamed_channels_are_not_guessed_at()
        => Assert.Null(HealthRules.MemoryLayout(WithMemory(4, M("DIMM 1", 16), M("DIMM 2", 32))));
}
