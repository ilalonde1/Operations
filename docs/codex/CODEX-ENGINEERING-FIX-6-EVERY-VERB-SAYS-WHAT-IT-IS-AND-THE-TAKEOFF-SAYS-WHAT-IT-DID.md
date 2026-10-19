# Engineering fix 6 of 7: every verb says what it is, and the takeoff says what it did

Bounded brief for Codex. Source edits and gates only. **Do NOT run `dotnet build` or `dotnet test`**: Claude verifies
on the dev box. Apply, read your own diff back, write the response, stop. No git operations beyond your own edits.

The response goes beside this file as `...-RESPONSE.md`. Reasoning: medium. Reading set, measured: about 60 KB,
because for most verb files only the first 30 lines are needed.

## Seven defects, each verified in the code on 2026-10-19

1. **Six verb files carry another verb's header**, left behind when `Program.cs` was split into one file per verb
   (commit 8fccbc25). All paths are under `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/`.

   | File | Its first lines describe | Which belongs to |
   |---|---|---|
   | `PublishVerb.cs` | "VERIFY a generated model against a trusted one…" | `e2k-compare` |
   | `PublishVerb.cs` | (also the `dxf-buildings` header) | `dxf-buildings` |
   | `CorpusReadVerb.cs` | "DXF -> ETABS — build a model from drafting's concrete-outline plan exports…" | `dxf-to-etabs` |
   | `VerifyE2kVerb.cs` | "INSPECT one plan: what does this drawing actually give the model?…" | `dxf-inspect` |
   | `MarkupListVerb.cs` | "THE LEDGER. Everything a page carries, by kind…" | `pdf-inventory` |
   | `SchedBorderVerb.cs` | "Vector front-end probe: read the NATIVE vector text + geometry…" | `vector-dump` |
   | `PerimVerb.cs` | "Core-wall concrete the estimator's way…" (and a sched-read header) | `wallconcrete` and `sched-read` |

   And six files have **no** purpose header: `DxfBuildingsVerb`, `E2kCompareVerb`, `PdfInventoryVerb`,
   `VectorDumpVerb`, `MeasureUsageVerb`, `VisionEstimateUsageVerb`.
2. **`wallconcrete` prices a millimetre wall as inches.**
   - `WallconcreteVerb.cs:55` reads `thicknessIn` straight from the vision JSON.
   - Line 75 prices `thks[i] / 12.0`.
   - `ScheduleConcreteReader.AsInches` (`Core/ScheduleConcreteReader.cs:44-48`) exists because the same reader returns
     some sizes still in millimetres ("a 300 mm wall reads 300"), and "keeps a 300 mm wall from being priced as a
     25-FOOT-thick one". `wallconcrete` never calls it.
3. **A comment contradicts the code beside it.**
   - `VectorTakeoffVerb.cs:534-536` says the schedule reads are "an INDEPENDENT CROSS-CHECK only — never added".
   - Line 537 adds schedule column concrete: `colFinalCy = schedColInputs.Count > 0 ? schedColCy + keptGrayColCy : colCyGeom`.
4. **Every `vector-takeoff` run ends with another building's benchmark.**
   `VectorTakeoffVerb.cs:585` prints "(suspended-slab benchmark: 31044 Coronation = 20,208 cy…)" whatever PDF was run.
5. **The IFC verb claims modelled rebar overrides the estimate; it does not.**
   `IfcTakeoffVerb.cs:30` prints "exact steel — overrides the density estimate where present". The value is only
   printed, and the reinforcing total stays the density estimate.
6. **The CLI rebar reader cannot see bar-list call-outs that the app's reader reads.**
   - `RebarCalloutExtractor` (the app's path) has `BarListRe` for `16-15M13.9`-style call-outs
     (`Core/RebarCalloutExtractor.cs:50-57`).
   - `RebarPdfReader` (the CLI's path, through `RebarChangeService.ComparePdfs`) has no such pattern: 0 matches.
   - So `takeoff rebar` and the app give different answers on bar-list sets such as Brewery District 30953.
7. **Dead constants.** `FootingScheduleReader.cs:44` declares `DimMinMm = 200, DimMaxMm = 6000`, and they are used
   nowhere.

## The fixes

1. **Move each header to the file of the verb it describes.**
   - Give each of the twelve files exactly one header for its own verb.
   - Where a file had none, write a two-line header from that verb's row in the help table (`Program.cs:253-332`),
     quoting its purpose text.
   - Change no code in these files.
2. **`wallconcrete` passes every thickness through the same millimetre guard.** Make `AsInches` internal, or move it
   to a small shared helper in Core, and call it at `WallconcreteVerb.cs:55`. One guard, not two copies.
3. **The comment at `VectorTakeoffVerb.cs:534-536` states what the code does.** Schedule columns replace the gray-fill
   columns on the floors the schedule covers. Schedule walls are a cross-check only (`:558`).
4. **Print the Coronation benchmark only when the PDF's file name carries `31044`.**
5. **The IFC line says the modelled rebar is reported beside the estimate, not used in place of it.**
6. **One bar-list grammar for both readers.**
   - Move `BarListRe` and its size bounds to an internal shared place in Core.
   - Have `RebarPdfReader` emit bar-list hits the way `RebarCalloutExtractor` does: the full token is the key.
   - The intensity grammars stay unchanged, so metric intensity sets (31065) read exactly as today.
7. **Delete the two dead constants.**

## Gates (authored, not run), in `Kor.Operations.EngineeringTools.Core.Tests/`

- `EveryVerbFileDescribesItsOwnVerb`: a source scan.
  - Each `TakeoffCli/Verbs/*Verb.cs` (not `*UsageVerb.cs`) has a header comment within its first 25 lines that
    contains its own verb name as registered in `TakeoffVerbs.cs`.
  - No file's header contains another registered verb's name as its first word.
  - It prints how many files it scanned.
- `TheTwoRebarReadersReadTheSameBarList`: feed one page of text containing `16-15M13.9` and `6-C15M08.5` to both
  readers; both report both tokens.
- `AMillimetreWallIsNotPricedAsInches`: the shared guard turns 300 into 300/25.4 and leaves 17.7 alone.

## Never read

`Baselines/`, `docs/etabs-handoff/`, any `.e2k`, `.dxf`, `.pdf`, `.png` or `.csv`.

## Read

- First 30 lines of each of the 12 verb files named in defect 1, plus `TakeoffCli/Program.cs` 250-335 and
  `TakeoffCli/TakeoffVerbs.cs`
- `TakeoffCli/Verbs/WallconcreteVerb.cs` (whole)
- `TakeoffCli/Verbs/VectorTakeoffVerb.cs` 525-590
- `TakeoffCli/Verbs/IfcTakeoffVerb.cs` (whole)
- `Core/ScheduleConcreteReader.cs` 30-60
- `Core/RebarPdfReader.cs` (whole, 10 KB)
- `Core/RebarCalloutExtractor.cs` (whole, 11 KB)
- `Core/FootingScheduleReader.cs` 35-50

## Response

Files changed, gates added, and one line per defect. Under 60 lines.
