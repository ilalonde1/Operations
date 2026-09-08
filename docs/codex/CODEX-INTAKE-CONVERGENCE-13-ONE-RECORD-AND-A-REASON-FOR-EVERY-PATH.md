# Codex 13 of N — one record for a sheet, one home for the readers, and a reason for every path

## Goal

Create the home the intake converges into — `Kor.Operations.EngineeringTools.Core/Intake/` — with
ONE entry point that reads a drawing PDF once and returns a typed record per sheet, and make the
geometry classifier say, for every vector path it is given, what became of it and why. Nothing the
intake emits may change: the twelve baseline DXFs below must come out byte-identical.

This is step 1 of the programme in `docs/PdfIntake.md`. Step 0 (commit 56470b5f) built the
instruments; this step builds the thing they measure.

## What the ledger says today

`takeoff pdf-inventory` on the five local stick files, every word and path counted once
(`docs/PdfIntake.md` §6): Unaccounted is 57–84% of every set. On 31130 it is 441,209 of 568,310,
and 411,762 of that is ONE row — "paths: not emitted (furniture, frame, grid, wall faces,
dimensions — undifferentiated)". The classifier decides per path and records nothing, so the
ledger cannot tell a grid line the furniture rule dropped (correct) from a wall face nothing read
(the largest structural gap there is: 0 walls against 892 in the Revit DXF of the same sheets).

The 76,448 paths it DID emit as "lines" are not knowledge either: a BEAM-layer polyline is a line
of unknown meaning, and the shear walls and basement walls on 31130 p11 are among them.

## The class, in one sentence

Every reader in this repo decides and forgets: `GeometryFilterService.Classify` has thirteen
discarding `continue` sites and four `Add` sites and records none of them, and every downstream
tool therefore starts from a DXF that has already lost the reason for everything it does not
contain.

## The home

    Kor.Operations.EngineeringTools.Core/Intake/          namespace Kor.Operations.EngineeringTools.Intake
      Disposition.cs        enum Disposition { Read, Discarded, Unread, Ignored, Unaccounted }
      PathFate.cs           what became of one path, and why
      WordFate.cs           what became of one word, and why
      SheetRecord.cs        everything read off one sheet, typed
      DrawingSetRecord.cs   the document: pages, producer, sheet index, the sheets
      DrawingIntake.cs      the ONE entry: Read(pdf, options) → DrawingSetRecord

`SheetInventory` (Core root) becomes a REPORT over a `SheetRecord` — it stops re-deriving. Its
`Disposition` enum moves to `Intake/Disposition.cs`; `SheetInventory.Disposition` is deleted and
callers use the Intake one.

## Change this

### 1. `Intake/PathFate.cs` and `Intake/WordFate.cs`

```csharp
public enum PathReason
{
    // Read
    BecameSlab, BecameColumnByDeclaredSize, BecameColumnByShape,
    // Unaccounted — emitted, meaning unknown
    EmittedAsLine,
    // Discarded, by the rule named
    MarkupOnlyMode, FurnitureRegion, GridAxis, Underline, PaperFill, SheetFrame, FrameEdgeLine,
    GridLineExcluded, ColumnTooSmall, UnfilledSmallShape, ColumnAspect, TooShort, TooFewPoints,
}
public sealed record PathFate(int PathIndex, Disposition Disposition, PathReason Reason, int? ObjectIndex);
```

`Disposition` is derived from the reason and nothing else: the three `Became*` are Read,
`EmittedAsLine` is **Unaccounted**, every other reason is Discarded. Put that mapping in one
static method, `PathFate.DispositionOf(PathReason)`, so nobody can disagree about it.

`WordFate(int WordIndex, Disposition Disposition, string Kind, string Reason)` carries the word
rules that live in `SheetInventory.Of` today (title block, schedule a reader read, schedule with
no reader, furniture box, grid bubble label, then the kind table). Move that logic, unchanged,
into `DrawingIntake`; `SheetInventory.KindOf` and the regex tables move with it and stay public.

### 2. `GeometryFilterService.Classify` records a fate for every path

Add one optional parameter at the end: `IList<PathFate>? fates = null`. At each of the seventeen
decision sites (the thirteen discarding `continue`s and the four `Add`s — `GeometryFilterService.cs`
lines 99–206 at HEAD; the declared-size column at :146 is an `Add` followed by its own `continue`)
record the fate with the path's index in the input list. Where a path reaches the end of the loop
without any site claiming it, record `TooFewPoints` or `TooShort`, whichever applies. **A test
asserts every input index appears in the fates exactly once.**

Nothing else in this method changes. Not a threshold, not an order, not a comparison.

### 3. `SheetRecord`

```csharp
public sealed record SheetRecord(
    int PageNumber, double WidthPts, double HeightPts, int Rotation,
    string? SheetNumber, string? BookmarkTitle, string SheetType,
    string? Level, string? Zone,                         // SheetTitleReader
    string? ScaleNote, int? ScaleDenominator,            // SheetScaleReader; the denominator the caller passed
    ExtractedGeometry Geometry,                          // slabs, columns, lines exactly as today
    IReadOnlyList<ScheduleTable> Schedules,              // column, footing, flat wall rows from today's readers
    IReadOnlyList<PlanMark> Marks,                       // mark-shaped words outside furniture, with position
    IReadOnlyList<SlabThicknessZoner.Callout> ThicknessCallouts,
    GridBubbles.Grid Grid,                               // what GridBubbles already finds; no longer thrown away
    SheetFurniture.Set Furniture,
    IReadOnlyList<MarkupNote> Markup,                    // annotation text + author, read HERE via PdfPig Annotation.Content
    int Links,
    VectorPageReader.PageContent Content,                // every word and path, kept for later passes
    IReadOnlyList<PathFate> PathFates,
    IReadOnlyList<WordFate> WordFates);
```

`ScheduleTable(string Heading, string Kind, IReadOnlyList<ScheduleRow> Rows)` where Kind is
`column | footing | shear-wall`, and `ScheduleRow(string Mark, IReadOnlyDictionary<string,string> Cells, string Route)`
built from the three existing readers' rows — no new reading. `PlanMark(string Text, double X, double Y)`.

Walls, footings-as-objects, dimensions, callouts, notes, storey heights are NOT fields yet. Each
arrives with its own step and its own reader; an empty list now would be a claim.

### 4. `DrawingIntake.Read`

```csharp
public static DrawingSetRecord Read(string pdfPath, IntakeRequest request);
public static SheetRecord ReadSheet(PdfDocument doc, int page, IntakeRequest request, DocumentFacts facts);
public sealed record IntakeRequest(int? ScaleDenominator, PdfIntakeOptions Options, bool MarkupOnly = false);
```

One `VectorPageReader.ReadPage(page, includeAnnotations: true, curveSegments: BezierSegments)` per
sheet, then in order: furniture, grid, title, scale, schedules (the three readers), marks,
callouts, geometry (`PdfPlanReader.ParsePage` → `Classify` with fates — only when a denominator
is given), word fates, annotations, links. `DocumentFacts` (bookmarks, outlines-present,
producer) moves from `SheetInventory` to `Intake/DrawingSetRecord.cs` unchanged.

### 5. The two verbs read from the record

- `pdf-takeoff` (`Program.cs` ~:62–177): build `DrawingIntake.ReadSheet` per page and export
  `record.Geometry`. Same flags, same console lines, same DXF bytes.
- `pdf-inventory`: build the record, and the path rows come from `record.PathFates` grouped by
  reason — one row per reason, primary. The single "not emitted — undifferentiated" row is gone.
  Word rows come from `record.WordFates`. Context rows stay.

### 6. Tests, in `Kor.Operations.EngineeringTools.Core.Tests/Intake/`

- `EveryPathHasExactlyOneFateTests`: synthetic `RawSubpath` lists exercising each of the sixteen
  sites; every index appears once; `DispositionOf` maps as stated.
- `TheLedgerChangesNothingButTheLedgerTests`: `Classify` with `fates: null` and with a list, on the
  same inputs, produces identical `ExtractedGeometry` (every slab point, column centroid, line
  point, colour, size). The rule-11 differential, in the build. Its summary must say what it
  compares and what it does not.
- `DrawingIntakeTests`: on a synthetic page with one column, one furniture box, one grid axis
  line and one free line, the record's Schedules/Marks/Grid/Fates are what the fixture drew.

## What NOT to do

- Do not change any threshold, order or comparison in `Classify`, in any schedule reader, in
  `SheetFurniture` or in `GridBubbles`. This step adds memory, not judgement.
- Do not read walls, footings, dimensions, storey heights, the sheet index or the scale any better
  than today. Steps 2 to 7.
- Do not touch `Kor.Operations.App`, `RebarPdfReader`, `PdfPageTextReader`, `PublishExplainers`
  or `SlabTakeoffEngine`. Step 8.
- Do not add a PDF library, a Python or PowerShell file, or a rule in KorStandards.
- Do not run the test suites; build `Kor.Operations.EngineeringTools.Core` and
  `Kor.Operations.EngineeringTools.TakeoffCli` once to confirm compilation. I run the tests.
- Repo only. No UNC path, no database, no share. The stick files are not needed for this step.
- Files touched outside the new `Intake/` folder and the new test folder:
  `GeometryFilterService.cs`, `SheetInventory.cs`, `Program.cs` — and nothing else.

## What I will check

1. **Twelve DXFs, byte for byte.** From the build before this step
   (`%LOCALAPPDATA%\Temp\kor-drawings\harness\step1-baseline\`), SHA-256 prefixes:

        95c16c772faa9baf 31065-01-p14   655068e3468f8432 31065-01-p15   13c1a4e123b3771b 31065-01-p16
        96b1fd9d36ab11db 31130-01-p11   7e862bf8b19d83da 31130-01-p12   74d1fb47c9c6e2be 31130-01-p13
        2799538c5ce44672 31138-01-p09   8804a067442c27d2 31138-01-p10   1df89a3e1fd4d6ab 31138-01-p11
        bea18da30bd9bec0 31168-01-p11   ecbf1efbf02cfe80 31168-01-p12   101db09654d955bc 31168-01-p13
        70a061993791e9f9 31202-01-p17

   Any difference fails the step.
2. **The ledger on 31130** (`takeoff pdf-inventory … --scale 96`): the undifferentiated row is
   gone; the per-reason Discarded rows plus `EmittedAsLine` plus the three `Became*` rows sum to
   the 490,872 inked paths counted today (411,762 not emitted + 76,448 lines + 1,557 slabs +
   1,105 columns; the 7,489 no-ink and 1,006 paper-fill paths are already their own rows);
   Unaccounted falls from 441,209 to the lines plus the unkinded words, about 106,000. The same
   on the other four sets, against their §6 rows.
3. `FiveStickFilesTests` 20 of 20, unchanged. Fast Core suite green. The three new test classes
   green.
4. `PdfInventory`'s JSON for the five sets re-banked and committed to the doc's §6 table.
