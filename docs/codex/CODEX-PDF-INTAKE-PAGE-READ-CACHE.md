# Codex — build task: a PDF page is walked once, ever; every read after that comes from the record

**Scope: this repository only, the files named below. Write code and its tests. No `dotnet build`, no
`dotnet test`, no drawing files, no database, no network.** Reading set 62 KB measured with `wc -c`.
Recommended reasoning: high — the record must reproduce the walk to the bit, and the six-set gate
(byte identity against banked models) is the proof. Work on the tree at `develop` HEAD (after
`048912f1`); Claude builds, runs the acceptance and reports back.

## The problem, measured

The corpus run builds 295 stick files (8,613 PDF pages) into ETABS models: run 11 on 2026-09-14 took
2 h 21 min at 6 workers, 776 CPU-minutes, **5.4 s a page**, and 30993-01 built in 874 s where a
recompose from its recorded views took 125 s — reading is about 85% of the cost. Every change to a
READING rule (what a filled band is, when two triangles are one rectangle, what a title's second line
is) invalidates the recorded views, so the whole read runs again: four such runs in two days.

But the expensive part of a read never changes. `VectorPageReader.ReadPage(Page, …)`
(`Kor.Operations.EngineeringTools.Core/VectorPageReader.cs`, 29 KB) walks the PDF's content stream
through PdfPig — every path, every subpath, every word — and a PDF is a fixed file. The rules that
change act on what the walk returns. And the walk runs TWICE per plan page: `DrawingIntake.ReadPage`
(`Core/Intake/DrawingIntake.cs` lines 98–140) calls it once unthinned (the population, every point
kept) and once more through `PdfPlanReader.ParsePage` (`Core/PdfToSafe/PdfPlanReader.cs` lines 46–75)
thinned at the sheet's scale for the classifier.

## What to build

**A record of the walk, per (PDF SHA-256, page number, curveSegments), from which both reads are
derived without opening the PDF.**

1. `VectorPageReader.RawPage` — what the walk saw before thinning or closure decided anything: page
   number, width, height (pts); the words (`TextToken`, as now); for every subpath in content order:
   its path ordinal, its subpath ordinal, every point after Bézier flattening at `curveSegments`
   (unthinned — `minPointDistance` null), whether a `Close` command was present, filled, stroked,
   line width, colour, `IsClipping`; and the annotation paths (`ReadAnnotationPaths`, line 273) as
   they are. Read the walk at lines 120–240 to see exactly what it keeps and drops: a subpath with
   fewer than 2 points is dropped and never gets a kept ordinal — under thinning MORE subpaths drop,
   so the raw record must keep every subpath that had ≥ 2 raw points and let the derivation drop.
2. `VectorPageReader.Derive(RawPage raw, bool includeAnnotations, double? minPointDistance,
   double? closeDistance, IList<int>? keptSubpathOrdinals)` → `PageContent`, reproducing the walk's
   result EXACTLY: `AddPoint`'s rule (a point within `minPointDistance` — `<=` — of the LAST KEPT point is
   dropped — a sequential filter over the raw points, lines 241–250), the `< 2 points → dropped`
   rule after thinning, the closure rule (line 216: with `closeDistance`, first–last closer than it →
   last point removed and closed; without, the 0.5 pt rule closes without removing), `ToGeomPath`,
   `IsClipping`/`PathOrdinal`, the kept-ordinal list, and annotations appended when asked. Then
   `ReadPage(Page, …)` = `Derive(Walk(page, curveSegments), …)` — the walk itself becomes one
   function, `Walk`, and the existing `ReadPage` overloads keep their signatures and results.
3. `PageReadCache` (`Core/PdfToSafe/PageReadCache.cs`): `RawPage GetOrWalk(string pdfSha256, int page,
   int curveSegments, Func<RawPage> walk)`, files under `DrawingMirror.Root/pages/<sha>/<page>-<curveSegments>.bin`
   (`Core/Dxf/DrawingMirror.cs`, 6 KB: `Root`, and how `SingleFile` hashes — reuse it, do not hash a
   file twice). Binary, `BinaryWriter` under `GZipStream`, a version byte first; on any read failure
   (missing, short, wrong version, exception) walk and rewrite. Doubles as doubles — the derivation
   must be bit-identical, so no float32, no rounding. Write to a temp name and rename, so a killed run
   leaves no half file. Read back after write and compare (rule 5), throw if it disagrees.
4. Wire it where the PDF is read for the intake: `DrawingIntake.ReadPage` gets the raw page ONCE and
   derives both reads (the unthinned population and the thinned classifier read); `PdfPlanReader.ParsePage`
   gets an overload taking a `RawPage`. The thirteen other `VectorPageReader.ReadPage(` callers (list
   them with grep; `SlabTakeoffEngine`, `PdfVersusDxf`, `SetStoreys`, `SetSchedules`, `AssemblySchedule`,
   `DrawingDigest`, `StickFileSlabThicknessReader`) are NOT changed in this task — say in the report
   which of them run inside the intake's read of a page (PdfOnlyBuild.WriteSheets → DrawingIntake) and
   would gain from the record next.
5. The cache key needs the PDF's SHA-256 once per file, not per page: `PdfOnlyBuild.WriteSheets`
   (`Core/Intake/PdfOnlyBuild.cs` lines 63–120) opens the document once; hash there and pass it down
   (`DocumentFacts`, which `Read` builds once per document and passes to every `ReadSheet`, is the natural carrier — read `DrawingIntake.cs` lines 20–45 and choose the
   smaller change). A caller with no hash (fixtures, a `Page` from nowhere) walks without recording.

## Tests (`Kor.Operations.EngineeringTools.Core.Tests/PdfToSafe/APageIsWalkedOnceTests.cs`)

- `Derive` against hand-built `RawPage`s: thinning drops a point within (`<=`) the tolerance of the last
  KEPT point (three points 0.4 apart at tolerance 0.5 keep the first and third); a subpath that thins
  to one point is dropped and absent from the kept ordinals while its neighbour keeps its ordinal; the
  closure rule both ways (with `closeDistance`: last point removed; without: closed at 0.5 pt and the
  last point kept); a Close command closes regardless; annotations appended only when asked; ordinals
  preserved.
- The cache: round trip of a `RawPage` with two subpaths and a word is bit-identical (compare doubles
  with `==`, not a tolerance); a wrong version byte or a truncated file walks again and rewrites;
  two calls with the same key walk once (count the delegate's calls).
- The class summary states WHAT THIS COVERS and WHAT IT DOES NOT (rule 11 of `CLAUDE.md`): it does
  not prove the walk equals the derivation on a real PDF — the six-set gate does, byte for byte.

## Read, in this order

1. `CLAUDE.md` rules 5, 7, 11 (about 3 KB of 15).
2. `VectorPageReader.cs` — whole (29 KB): `TextToken` (32), `GeomPath` (39), `PageContent` (59), the
   `ReadPage` overloads (91–126), the walk (160–240), `AddPoint` (241–250), `ToGeomPath`,
   `ReadAnnotationPaths` (273).
3. `PdfPlanReader.cs` lines 40–80 (2 KB): `ParsePage`, the scale-dependent tolerances.
4. `DrawingIntake.cs` lines 20–45 and 98–140 (4 KB): `ReadSheet`, `ReadPage`, the two reads.
5. `PdfOnlyBuild.cs` lines 63–120 (3 KB): where the document is opened and pages are read.
6. `DrawingMirror.cs` (6 KB): `Root`, `SingleFile` and its hashing.
7. `Kor.Operations.EngineeringTools.Core.Tests/Intake/SixSetReadCache.cs` (7.7 KB): the pattern of a
   read cache in this repo — manifest, read-back, reasons for a miss.

## Rules for the change

- The walk's result must not change by a bit: same points in the same order, same ordinals, same
  closure, same annotations. The six-set gate compares the finished models byte for byte against the
  bank and is the acceptance; if you find a place where the derivation CANNOT reproduce the walk
  (a rule that depends on something the raw record does not hold), record it in the report and keep
  that rule in the walk rather than approximate it.
- Warnings are errors repo-wide; xUnit analyzers on (`Assert.Equal(expected, actual)`).
- Rule 7: no regex and no Windows path through a non-raw string.
- No new NuGet package; `System.IO.Compression` is in the BCL.
- Do not touch `Baselines/`, the six-set test, `CorpusAnalyzer.cs`, or any reader rule in
  `GeometryFilterService.cs`.

## Acceptance (Claude runs; you do not)

Fast suite green; the six-set gate on a cold page cache (reads, byte-identical, and the cache is
written); the six-set gate again with the read cache manifest deleted so it must READ, on a warm
page cache (byte-identical, and the read is measured — the number the brief exists for); one corpus
set (30993-01, 123 pages) built cold then warm, timed.

## Output

The code, in place, and `docs/codex/CODEX-PDF-INTAKE-PAGE-READ-CACHE-RESPONSE.md`: what changed
(files, members), the record's exact binary layout, which of the thirteen other callers run inside
the intake's page read, and anything the derivation could not reproduce.
