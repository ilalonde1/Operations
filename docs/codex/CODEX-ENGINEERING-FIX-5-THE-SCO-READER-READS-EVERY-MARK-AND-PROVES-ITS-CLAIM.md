# Engineering fix 5 of 7: the S-Concrete reader reads every mark, writes back what it read, and proves its claim

> ⛔ **WITHDRAWN 2026-10-19 — DO NOT RUN.** Superseded by the plan of 2026-10-19, which reissues this fix in its
> place in the order, against the baseline taken first. Kept as a record.

Bounded brief for Codex. Source edits and gates only. **Do NOT run `dotnet build` or `dotnet test`**: Claude runs the
gates on the dev box against the real files. Apply, read your own diff back, write the response, stop. No git
operations beyond your own edits.

The response goes beside this file as `...-RESPONSE.md`. Reasoning: medium. Reading set, measured: 50 KB, the four
files listed at the end, plus `LiveProjects.cs`.

## Ground truth, measured by Claude on 2026-10-19 (you cannot reach these files; use these values)

- **The files.** 30961-01 holds 31 hand-made `.SCO` files in
  `LiveProjects.Folder("30961", @"05 Column Design\Column Design - AEM\S-CONCRETE")`.
- **The workbook they were typed from.** It sits one folder up, in `Column Design - AEM`, and is called
  `30961-01 SEC ELEMS.xlsm`.
- **The `Calculator` worksheet.**
  - Row 1 is the header: `Story | Label | Section | Load Combo | N | T | Vz | My | Vy | Mz | Type | Remarks`, in
    columns A-L.
  - Row 2 is blank. Data runs from row 3 to row 15,287.
  - `Remarks` (column L) is the S-Concrete Comment, written in full.
  - The `.SCO` Comment is that text **cut short**: S-Concrete truncates near 60 characters. So a `.SCO` row belongs to
    the Calculator row whose `Remarks` **starts with** its Comment.
- **The reconciliation, re-run today with exactly that rule:**
  - 31 files, 1,665 demands, 1,665 matched, 0 with no Calculator row, 0 comments matching more than one distinct row.
  - 9,990 values compared as N→Nf, T→Tf, Vz→Vfz, My→Mfy, Vy→Vfy, Mz→Mfz.
  - 0 differ by more than 0.1%, and the worst absolute difference is 0.0005, which is print rounding.
  - A real Comment: `L02TH  C21 -> EQX2, 14X36, 55Mpa, kl 8.84997,Cm-1, Slen Min-` (cut there by S-Concrete).

## Five defects, verified in the code

1. **The test named for this proof never opens the workbook.**
   `SConcreteFileTests.TheHandMadeFilesOn30961MatchTheWorkbookTheyWereTypedFrom` (around lines 257-305) asserts only
   that there are more than 1,000 demands, that identities are non-blank, and that there are no conflicting duplicates.
2. **A mark is assumed to be `C` plus digits.**
   - The identity pattern at `SConcreteFile.cs:76-79` is `(?<mark>C\d+)`.
   - KOR schedules use `PC1`, `C02-A`, `GC11-C` and `TC04`. Their rows lose their identity, or are dropped when there
     is no filename to fall back on.
3. **Writing loads back rewrites the engineer's comment.**
   - `Comment(ColumnDemand)` (around 273-277) rebuilds the text and drops the tail the engineer wrote
     (`, Slen Min-NO, Maj-NO`).
   - For a convention-B file (`31021-01`: every Comment is `--` and the identity is the filename), it would replace `--`
     with a malformed `"<storey>  <file stem> -> LC1, , "`.
4. **Conflicts compare axial force only.**
   - `ColumnDemandSchedule.cs` around 78-83 groups by `Math.Round(Nf, 3)`.
   - Two files can disagree on every moment and shear and still be reported as agreeing.
5. **The two share tests pass when the share is unreachable.** Fix 4 converts these to skips. Here, only make sure any
   new share test follows that form: `[SkippableFact]` with `Skip.IfNot(LiveProjects.ShareReachable, ...)`.

## The fixes

1. **A real reconciliation test**, in `SConcreteFileTests.cs`:
   - Name it `EveryHandMadeFileOn30961MatchesTheCalculatorRowItWasTypedFrom`, `[SkippableFact]`, `[Trait("Speed","Slow")]`.
   - Open the `.xlsm` with ClosedXML (already referenced by Core) and build the `Calculator` rows from row 3.
   - Join each `.SCO` demand to the row whose `Remarks` starts with its Comment, using the rule above.
   - Assert, printing the counts: every demand matched; 0 comments match more than one distinct row; every one of the
     six forces is within `0.001 * max(1, |calc|)`.
   - Keep the existing test, renamed truthfully to `TheHandMadeFilesOn30961ReadAsWellFormedDemands`.
2. **A mark is whatever token stands before `->`.** Change the identity pattern to
   `^\s*(?<storey>\S+)\s+(?<mark>\S+)\s*->\s*...` with the rest unchanged, so the storey and the mark are the two tokens
   before the arrow.
3. **What was read is written back as it was.**
   - Add an optional `RawComment` to `ColumnDemand`, set by `ReadDemands`.
   - `WithDemands` writes `RawComment` when present and unchanged in identity. Otherwise it writes `Comment(d)`.
   - If every row in the file being written into had `--`, the new rows get `--`.
4. **A conflict is any of the six forces disagreeing.**
   - Group by the six rounded values.
   - `DemandConflict` reports which force differed most, its two values and its percent.
   - Keep `Percent` meaning "largest relative difference across the six".
   - Keep the trivial/material split (under 1% is trivial) on that largest difference.

## Gates (authored, not run)

- **`SConcreteFileTests`:**
  - `AMarkIsTheTokenBeforeTheArrow`: `PC1`, `C02-A`, `GC11-C` and `TC04` each read with storey and mark intact.
  - `WritingBackKeepsTheEngineersComment`: read a file whose comment carries `, Slen Min-NO, Maj-NO`, write it back,
    and the comment is byte-identical.
  - `AFileThatRecordedNoIdentityStillRecordsNone`: a `--` file written back keeps `--`.
- **`ColumnDemandScheduleTests`:** `TwoFilesThatAgreeOnAxialButNotMomentConflict`, with the conflict naming `Mfy`.
- **The reconciliation test above.**

## Never read

Anything on the share, `Baselines/`, any `.e2k`, `.dxf`, `.csv` or `.xlsm`.

## Read

- `Kor.Operations.EngineeringTools.Core/SConcreteFile.cs` (whole, 15 KB)
- `Kor.Operations.EngineeringTools.Core/ColumnDemandSchedule.cs` (whole, 14 KB)
- `Kor.Operations.EngineeringTools.Core.Tests/SConcreteFileTests.cs` (whole, 14 KB)
- `Kor.Operations.EngineeringTools.Core.Tests/ColumnDemandScheduleTests.cs` (whole, 7 KB)
- `Kor.Operations.EngineeringTools.Core.Tests/LiveProjects.cs`

## Response

Files changed, gates added, and one line per defect. Under 50 lines.
