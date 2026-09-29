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
// level (never a whole provider); the GPU count is de-duplicated by report; memory-layout on 206-N's
// real module set and on balanced/single-stick edges.
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

    [Fact]
    public void Gpu_resets_are_counted_once_per_report()
        => Assert.Matches(new Regex(@"GpuHang\s*=\s*Summ\s*\(\$gpuWer\s*\|\s*Group-Object"), Probe);

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
