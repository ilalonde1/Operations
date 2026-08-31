# CODEX — ARCHITECTURE MAP: FIX THE AUDIT FINDINGS

> **IMPORTANT: Do NOT run `dotnet build` or `dotnet test`.**
> Verification happens on the dev box on Claude's side. Your test runner consistently hangs here for
> 15+ minutes and spawns orphan dotnet processes that lock build artifacts. Apply the edits, grep
> your own diff if useful, then ping. Stop there.
>
> **Do NOT run any destructive git operation** — no `git clean`, no `git reset --hard`, no force
> push. **Do NOT regenerate `docs/architecture/architecture.json`** — that means running the tool.
> Claude regenerates it and checks the numbers.

Your audit at `docs/codex/CODEX-ARCHITECTURE-MAP-AUDIT-RESPONSE.md` was verified line by line
against source. **Every Critical and Material finding was confirmed** — nothing was invented. Fix
them, in the order below. Files: `Kor.Operations.Architecture/{Program,Graphs,Scripts,VisioRenderer}.cs`,
`Kor.Operations.Architecture.Tests/`, and `Kor.Operations.App/Kor.Transmittals.App.Tests/ArchitectureMapIsCurrentTests.cs`.

**Each fix gets a test in `Kor.Operations.Architecture.Tests` that fails without it.** A fix to a
measurement with no test is how all of these got in.

---

## 1. Determinism — invariant 1, currently broken two ways

**M1, culture.** `Graphs.cs:49` builds a node `Detail` with `$"{p.Lines:N0} lines"`, which is
CurrentCulture, and that string is serialised into the committed model. Under `fr-CA` the same
source produces different bytes. Sweep the whole component for any other culture-sensitive
formatting that reaches the model **or a Visio formula string** — not just this one instance.

**M6, partial types.** A type id is project + namespace + simple name, so all parts of a partial
class collapse onto one id: `FinancialMetricDefinitions` appears **12 times**, `BrochureBuilderViewModel`
8 times, six ids in total. Two consequences — `declarations[id]` is last-writer-wins so
`Directory.EnumerateFiles` order decides which declaration feeds the similarity score, and the
`Types` array carries duplicate ids whose relative order is enumeration order.

Requirement: **one entry per type, and the same bytes out on any machine.** A partial type is one
type; its declaration for comparison purposes is all of its parts, combined in an order that does
not depend on how the filesystem enumerated them. Line counts and the `File` field should describe
the whole type, not whichever part happened to be last. How you represent that is your call — say
what you chose in the diff.

## 2. The instrument still measures itself — third occurrence

**C2.** `Kor.Operations.Architecture.Tests/ExtractorTests.cs` contains the word `Deltek` (it asserts
Deltek has real evidence), so the map now cites the mapper's own test file as evidence this
repository talks to a production database, and the Relationships graph draws
`Kor.Operations.Architecture.Tests → ext:Deltek Vision (ODBC)`.

The source filter and the project filter both exclude `Kor.Operations.Architecture/` and neither
excludes the test project. **The existing test `TheInstrumentDoesNotMeasureItself` has the same
narrow guard, so it passes while this is true** — it is wrong in precisely the way the code is.

Requirement: nothing belonging to this tool — either project — is ever evidence about the system.
Prefer one derived rule over two hard-coded strings, so the next project added alongside is covered
without anyone remembering to. Widen the test so it would have caught this.

## 3. The staleness gate does not cover what the drawing prints

**C1.** The committed model records `Kor.Operations.Architecture.Tests` as 1 file / 148 lines; the
tree has 2 files. The gate stayed green because it compares only project names and project
references.

My original reasoning for that scope was *"adding one file to one project moves no box on the
diagram"* — **and that reasoning was wrong**. The master matrix prints a line count in every row
label and type-by-role counts in every cell, so a new file does move the drawing.

The resolution is not to gate on line counts: those change on every edit, and a test that fails on
every edit gets turned off within a week — which is the reason the scope was narrow in the first
place. **Gate on the structural facts instead: which projects exist, what each references, and how
many source files each contains.** File count changes when a file is added, removed or moved — a
real event — and not when someone edits a method body.

Then say plainly, in the test's own comment, that line-count drift is cosmetic and is refreshed on
the next regeneration. An unstated limit is the thing being fixed here; do not replace it with a
different unstated limit.

## 4. Measures that are wrong rather than merely narrow

**M3, verbs.** `CliVerbs` recognises only `args[0].Equals("…")`. Seven live verbs are invisible:
`sector`, `emit`, `ensure` in `tools/BdSynthesisSmoke`, `docx`, `all`, `pursuit` in
`tools/BdSectorSmoke`, `pages` in `tools/MerxProbe` — all written `args[0] == "…"`. Handle the
comparison forms and a `switch` on `args[0]`. Keep reading it off the **syntax tree**: a grep for
string literals would collect anything that merely looks like a verb, which is why it is written
this way.

**M5, cycles.** `Program.cs:635` abandons a path at depth 12, so a loop of 13 or more projects is
reported as no cycle at all. Zero cycles is currently one of this tool's headline claims. Make it
true for any graph, and pin it with a test using a synthetic long cycle rather than only asserting
that today's real graph is empty.

**M4, format ownership.** 80 of the format edges are owned by **test** types, so the "which project
handles which file format" matrix credits projects that only test or sit beside the code that
handles a format. Keep the edges in the model — they are true statements about types — but the
matrix and the master sheet must not present a test as a handler.

## 5. Ownership versus what actually compiles

**M2.** File ownership is by nearest physical directory. `Kor.Operations.Data/SqlTimeouts.cs` is
`Compile Include`d into `Kor.EmailSearch.Core`, `Kor.Operations.Business` and `Kor.Operations.App`,
and the map shows none of that. This understates rather than falsifies, but the map's whole claim is
that it describes the system as it is built.

Read `Compile Include` items from each `.csproj` and let the map reflect that a file can be compiled
by more than one project. If you judge the full fix disproportionate, implement the smaller honest
thing — record linked items and state the limitation in the model — and say which you chose and why.

## 6. The three Minors

- Script `ReferencedBy` counts substring mentions in any candidate file, including Markdown and
  JSON, so `> 0` does not mean "wired in". The page is titled *Scripts nothing references*, which is
  the conservative direction and fine; make the subtitle say what the number actually measures.
- `File.ReadAllText` / `File.ReadLines` decode with defaults. Either state the assumption or handle
  a non-UTF-8 file deliberately.
- The renderer disables `ScreenUpdating`, `EventsEnabled` and document undo and restores only
  `DeferRecalc`. With `--keep-open`, or on an exception, a visible Visio is left with events and
  painting off. Restore what you turned off, on both paths.

---

## Constraints

- **The page set stays at 10.** Application, Drawing intake, Matrix - dependencies, Matrix - formats,
  CLI verbs, Duplication, Master matrix, Nooks and crannies, Relationships, Recipes.
- **No new NuGet packages.** Roslyn 4.5.0 is what is in the local cache and there may be no network.
- **Extraction stays independent of the solution compiling** — syntax trees only, no MSBuildWorkspace,
  no Build.Locator.
- Numbers will move when Claude regenerates. That is expected. What must not move between two runs
  on the same source is anything at all.

When you ping, list what you changed per finding and what you chose where the brief left it open.
