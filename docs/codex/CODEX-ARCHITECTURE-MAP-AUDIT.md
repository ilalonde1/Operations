# CODEX — ARCHITECTURE MAP: COMPLETE ADVERSARIAL AUDIT

> **IMPORTANT: Do NOT run `dotnet build` or `dotnet test`.**
> Verification happens on the dev box on Claude's side. Your test runner consistently hangs here for
> 15+ minutes and spawns orphan dotnet processes that lock build artifacts. Read the source, reason
> about it, and write the report. Nothing else.
>
> **Do NOT run any destructive git operation** — no `git clean`, no `git reset --hard`, no force
> push, no deleting files. `git log`, `git show`, `git diff` for reading only.

You are a **hostile reviewer**. Find what is wrong, not what is right. Do not congratulate. Every
claim cites `file:line`. If you cannot cite it, do not claim it.

---

## What this component is

A tool that **derives a map of this repository from its own source** and draws it into Visio. It
exists because a hand-drawn architecture diagram is wrong the week after it is drawn, and a
confidently wrong map is worse than none.

    source ──(Extractor, Roslyn syntax trees)──▶ docs/architecture/architecture.json
           ──(VisioRenderer, COM)──────────────▶ KOR-Application-Map.vsdx + one .png per page

Five commits, `8a837374..54a6f5c4`, 2,587 lines. Read them with `git show` / `git diff`.

    Kor.Operations.Architecture/Program.cs          model records, Extractor, analysis passes, CLI
    Kor.Operations.Architecture/Graphs.cs           force-directed and layered layout
    Kor.Operations.Architecture/Scripts.cs          inventory of the parts that are not C#
    Kor.Operations.Architecture/VisioRenderer.cs    every page, over IDispatch on `dynamic`
    Kor.Operations.Architecture.Tests/              10 tests
    Kor.Operations.App/Kor.Transmittals.App.Tests/ArchitectureMapIsCurrentTests.cs
    tools/New-ArchitectureMap.ps1                   launcher

Current output on this repo: 63 projects, 2,451 types, 7,178 mention edges, 379 format edges, 10
external systems, 46 CLI verbs, 583 scripts, 38 non-boilerplate duplicate names, 0 dependency
cycles, 10 pages.

---

## The invariants that must hold

Attack these first. Each has already been violated once during construction, which is why they are
written down rather than assumed.

1. **The output is deterministic.** Same source in, byte-identical `architecture.json` out — on any
   machine, in any locale, on any run. The model is committed to git precisely so that its diff
   shows the architecture moving; anything that makes it churn destroys the entire point.

2. **The instrument never measures itself.** The extractor's own marker table once registered as
   evidence that this repo talks to Visio, Excel and Revit. Its own output file, read back on the
   next run, once made all 125 unreferenced PowerShell scripts appear referenced. **Find any
   remaining path by which the tool's existence or output changes what it reports.**

3. **A number that is reported must be defensible.** "Referenced by nothing" was reported for 252
   SQL migrations that are the live schema of a production database, because a runner applies them
   by ordinal and nothing names them. **Find every other measure that is technically true and
   practically a lie.**

4. **A stated limitation is honest.** A mention edge is "this file names that type"; 351 names that
   resolve to more than one type are counted and dropped rather than guessed. Check the stated
   limits match the code, and that no OTHER limitation is silently unstated.

5. **Failing to draw must not lose the extraction.** The model is the durable artefact.

---

## Where to look hardest

Not a checklist to tick — the areas where the design is thin. Go wherever the code takes you.

**Correctness of the measures.** `Similarity` in `Program.cs` is a token-level LCS with a 20,000
token cap and a `NormaliseDeclaration` that strips `//` lines. What does it do with block comments,
with attributes, with generated code, with a type declared twice in one file, with `#if`? Is the
`2·LCS/(len+len)` ratio the right shape? What does the cap do at the boundary rather than past it?
`Duplicates` groups by **simple name**, so nested types and generic arities collide — does that
produce a wrong number anywhere?

**Culture and encoding.** Every number that reaches a Visio formula or the JSON must be
locale-independent. Find any that is not. Files are read with default encoding — what happens to a
source file that is not UTF-8?

**The script inventory** (`Scripts.cs`). A reference is a case-insensitive substring match of a bare
filename against every candidate file. Two scripts sharing a filename in different folders, a
filename that is a substring of a longer one, a name that appears in a comment or a changelog, a
script referenced only by a variable — which of these does it get wrong, and in which direction? The
`Migration` and `Vendored` regexes are heuristics; where do they over- or under-match?

**COM lifetime** (`VisioRenderer.cs`). Roughly 9,000 shape and cell RCWs are created and only the
top-level application object is released. What happens on an exception mid-render, on a page that
throws, on a second run while Visio is already open? Can this leave an orphan `VISIO.EXE`? The
automation switches (`ScreenUpdating`, `EventsEnabled`, `DeferRecalc`, `UndoEnabled`) are turned off
and only `DeferRecalc` is turned back on — does anything downstream depend on the others?

**Scaling.** `ForceDirected` is O(n²) per iteration over 700 iterations. `Similarity` is O(n·m) per
pair. `ScriptInventory` is O(candidates × names). At what repository size does each become
unusable, and does anything warn before that?

**The freshness test.** `ArchitectureMapIsCurrentTests` deliberately checks only which projects
exist and what they reference — not file or type counts, because a test that fails on every new
source file gets turned off within a week. **Is that the right line?** Name a realistic change that
moves the picture and that this test would not catch.

**The tests themselves.** 10 tests over 2,587 lines. Which of the invariants above is NOT actually
pinned by a test? Which test would still pass if its subject were broken? The renderer test skips
when Visio is absent — what does that hide?

---

## What to report

    CRITICAL   wrong output, silent data loss, or a number a person would act on and be misled by
    MATERIAL   a real defect with a plausible trigger
    MINOR      worth fixing, no consequence today

For each: `file:line`, what is wrong, the input or condition that triggers it, and what the
consequence is. **No suggested patches** — the diagnosis is what is wanted.

Then, separately:

- **Exactly one ship-blocker.** The single thing that most needs fixing before this is trusted. One.
- **What is MISSING.** A measure that should exist and does not; a part of the system the map still
  cannot see. The map claimed this repo was 62 projects of C# until scripts were counted — what is
  the next thing of that kind?
- **Anything you believe is right.** One short paragraph, last, and only if you mean it.

Write the report to `docs/codex/CODEX-ARCHITECTURE-MAP-AUDIT-RESPONSE.md`. Ping when done.
