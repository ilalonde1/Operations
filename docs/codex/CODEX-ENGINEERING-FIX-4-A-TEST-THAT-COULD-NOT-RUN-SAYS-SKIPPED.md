# Engineering fix 4 of 7: a test that could not run says SKIPPED, not PASSED

> ⛔ **WITHDRAWN 2026-10-19 — DO NOT RUN.** Superseded by the plan of 2026-10-19, which reissues this fix in its
> place in the order, against the baseline taken first. Kept as a record.

Bounded brief for Codex. Source edits only. **Do NOT run `dotnet build`, `dotnet restore` or `dotnet test`**: Claude
restores, builds and runs both suites on the dev box. Apply, read your own diff back, write the response, stop. No git
operations beyond your own edits.

The response goes beside this file as `...-RESPONSE.md`. Reasoning: medium.

**Reading rule for this brief: read the four helper files whole, and in every other file only the 25 lines around
each hit of the search below.** That keeps the reading set near 90 KB.

## What is wrong, counted on 2026-10-19

Tests that need the projects share, a Desktop PDF or a local drawing mirror check for it, write "SKIPPED" to the test
output, and then **`return`**. xUnit records a `return` as a **pass**, and neither test project uses any skip
mechanism: `SkippableFact`, `Skip.If` and `Assert.Skip` appear 0 times.

So when the share is unreachable, a suite full of gates reports green having checked nothing. The gates that guard the
banked models — ModelCoverage, StackMerge, ShippedModelsAgree, the 30961 `.SCO` reconciliation — are among them.

**The search that finds every site.** Run it yourself first and work from its output:

    grep -rnE "SKIPPED|ShareReachable\)|BuildOrSkip|if \(!(File|Directory)\.Exists\(" --include=*.cs \
      Kor.Operations.EngineeringTools.Core.Tests Kor.Operations.App/EngineeringTools.Tests

On 2026-10-19 that matched in 33 files: 20 in Core.Tests and 13 in the App's EngineeringTools.Tests. The four
helpers return null rather than skip: `GeneratedModel.cs` (`BuildOrSkip`), `Intake/SixSetReadCache.cs`,
`DrawingCache.cs`, and `LiveProjects`, wherever it lives.

## The fix

1. **Add the `Xunit.SkippableFact` package (version 1.4.13)** to both test projects'
   `.csproj`: `Kor.Operations.EngineeringTools.Core.Tests` and `Kor.Operations.App/EngineeringTools.Tests`.
2. **Every test that can be skipped says so.**
   - Turn each `[Fact]` / `[Theory]` whose body returns early on a missing share, file or folder into
     `[SkippableFact]` / `[SkippableTheory]`.
   - Replace the early return with `Skip.If(<condition>, "<the reason it already prints>")` or `Skip.IfNot(...)`.
   - Keep any `[Trait]` (`Speed=Slow` and others) exactly as it is.
3. **Helpers may still return null, but a test that receives null skips.** For example
   `var built = GeneratedModel.BuildOrSkip(...); Skip.If(built is null, "drawings for <job> not reachable");`.
   Rename `BuildOrSkip` only if every caller is updated in the same edit.
4. **Leave alone any test that already fails, rather than returns, when its input is missing.**
   `CompiledDefaultsAreTheBankedRows` "never skips" by design. Do not soften any test that fails today.
5. **Change no assertion.** This brief changes how an untested run is *reported*, not what a run tests.

## Gate (authored, not run)

`ATestThatCannotRunIsSkippedNotPassed`, in `Kor.Operations.EngineeringTools.Core.Tests/`. A source scan over both test
projects' `.cs` files. For every method carrying `[Fact]` or `[Theory]` (not the `Skippable` forms), its body contains
none of these:
- `SKIPPED`
- `ShareReachable) return`
- `if (!File.Exists(...)) return`
- `if (!Directory.Exists(...)) return`

The test names each offender with its file and line. It lists the files it scanned and how many methods, so a scan
that found nothing cannot pass silently.

## Never read

`Baselines/`, `docs/etabs-handoff/`, any `.e2k`, `.dxf`, `.pdf` or `.csv`.

## Response

The package lines added, then a table of file, number of tests converted and number of helper call sites changed,
with totals. Under 70 lines.
