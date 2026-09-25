using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A REFUSAL THAT DOES NOT SAY WHAT TO DO IS HALF A MESSAGE (2026-09-24).
///
/// Ian: "I want EXPLICIT reasons a model didn't run and how to fix it in the error message.
/// Everything needs to be explicit - questions / errors - everything."
///
/// The line that prompted it appeared 59 times across the corpus and read, entire:
///
///     &lt;sheet&gt;: no structural outlines found on the expected layers — not placed.
///
/// A whole drawing is dropped on that sentence. It names no layer the reader wanted, no layer the
/// drawing has, and nothing anyone could do about it. The engineer reads it, cannot act, and the
/// structure stays missing. It now names both sets of layers and the two ways to fix it — the run
/// flag and the KorStandards key — or says plainly that a notes sheet drawing no structure is not a
/// fault at all.
///
/// This test is the general form, because one fixed message is not a method. Every line that tells
/// the engineer something was LOST — not placed, not modelled, has no plate — has to carry an
/// instruction with it.
///
/// ⚠ WHAT THIS COVERS AND WHAT IT DOES NOT (rule 11). It is a SOURCE SCAN over the message literals
/// in the engine, matching a small set of loss phrases and requiring an action phrase nearby. So:
///
///   - it cannot tell a GOOD instruction from a useless one. "check this location" satisfies it and
///     helps nobody; the words are necessary, not sufficient.
///   - it only sees messages built as literals in these files. A message assembled from pieces, or
///     one that reaches the engineer through the workbook rather than the report, is invisible to it.
///   - it says nothing about whether the stated fix WORKS. Naming a flag that does not exist would
///     pass.
///
/// The exemption list is the honest part: the loss messages that still have no instruction are named
/// below with what each would need. It may only shrink.
/// </summary>
public class EveryRefusalSaysHowToFixItTests
{
    private readonly ITestOutputHelper _out;

    public EveryRefusalSaysHowToFixItTests(ITestOutputHelper output) => _out = output;

    /// <summary>The engine's own message sources — where a refusal the engineer reads is written.</summary>
    private static readonly string[] Sources =
    {
        "Dxf/DxfToEtabsService.cs",
        "Dxf/StructuralPlanClassifier.cs",
        "Dxf/E2kGeometryComposer.cs",
    };

    /// <summary>A line that tells the engineer something was lost.</summary>
    private static readonly Regex Loss = new(
        @"NOT PLACED|not placed\.|was not modelled|were not modelled|not modelled,|The storey has no plate",
        RegexOptions.Compiled);

    /// <summary>A line that tells her what to do about it.</summary>
    private static readonly Regex Action = new(
        @"TO FIX|check this location|give (them|it) for this run|--[a-z-]+|dxf\.[a-z-]+|"
        + @"say so and|answer the .* question|add a plate|if this sheet|tell us",
        RegexOptions.Compiled);

    /// <summary>
    /// Loss messages that still carry no instruction, each with what it would need.
    ///
    /// Named rather than silent, so the gap is countable and removing one is a deliberate act. This
    /// list may only shrink — a new refusal does not get added, it gets an instruction.
    /// </summary>
    private static readonly Dictionary<string, string> NoInstructionYet = new(StringComparer.Ordinal)
    {
        ["The storey has no plate"] =
            "the perimeter-wall fallback's two refusals. They now say WHY (the paint-to-wall "
            + "comparison, a region under the smallest plate, and so on) but not what the engineer "
            + "can do — because nobody knows yet: see CODEX-THE-FLOOR-THE-WALLS-DO-NOT-CLOSE.md, "
            + "where two candidate rules are measured dead. Until that is answered the honest "
            + "instruction is the workbook question, not a fix.",
    };

    [Fact]
    [Trait("Speed", "Fast")]
    public void EveryMessageThatSaysSomethingWasLostAlsoSaysWhatToDo()
    {
        string root = Path.Combine(RepoRoot(), "Kor.Operations.EngineeringTools.Core");
        var silent = new List<string>();
        int checkedLines = 0;

        foreach (string rel in Sources)
        {
            string path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;

            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;   // prose, not a message
                if (line.TrimStart().StartsWith("///", StringComparison.Ordinal)) continue;
                if (!Loss.IsMatch(line)) continue;

                checkedLines++;

                // the message may be built over several lines: look at the statement around it
                int from = Math.Max(0, i - 8), to = Math.Min(lines.Length - 1, i + 12);
                string around = string.Join("\n", lines[from..(to + 1)]);

                if (Action.IsMatch(around)) continue;
                if (NoInstructionYet.Keys.Any(k => line.Contains(k, StringComparison.Ordinal))) continue;

                silent.Add($"{rel}:{i + 1}  {line.Trim()[..Math.Min(100, line.Trim().Length)]}");
            }
        }

        _out.WriteLine($"{checkedLines} loss message(s) checked; {NoInstructionYet.Count} class(es) exempt.");

        Assert.True(
            silent.Count == 0,
            "These messages tell the engineer something was lost and not what to do about it:\n  "
            + string.Join("\n  ", silent)
            + "\n\nEvery refusal states, in the message itself: what was lost, WHY it was lost, and "
            + "the action that would change it — a run flag, a KorStandards key, a drawing change, or "
            + "an answer in the workbook. If there is genuinely no action, say that plainly and add "
            + "the class to NoInstructionYet with the reason.");
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void TheListOfRefusalsWithoutAnInstructionNeverGrows()
    {
        Assert.True(
            NoInstructionYet.Count <= 1,
            $"NoInstructionYet holds {NoInstructionYet.Count} entries and may hold at most the one "
            + "that existed when this gate was written. A new refusal gets an instruction, not an "
            + "exemption.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.Core")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
