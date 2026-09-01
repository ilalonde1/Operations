### Per-building columns still span through real storeys, so the split does not fix the overlap she complained about
SEVERITY  EMBARRASSING
WHERE     Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs:1019
WHAT      `FreeStoreysFor` returns every storey crossed by the member, but the writer emits one ETABS column with `{colSpan}` in the `LINE` record and only one `LINEASSIGN` on `colStory.Name`. The publish invariant only checks objects assigned to multiple storeys, not objects whose connectivity span is greater than one.
TRIGGER   `31168-A/B/C-FROM-DRAWINGS.e2k` after the per-building split, especially the LEVEL 1 to LEVEL 2 columns crossing LEVEL 1 MEZZ.
CONSEQUENCE  Andrea opens LEVEL 1 / LEVEL 1 MEZZ and still sees columns running through the mezzanine while mezzanine columns occupy the same space. That is the exact "overlap" failure she said makes the model unusable.
VERIFY    Run `Select-String '^\s+LINE\s+"KC\d+"\s+COLUMN\s+"KP\d+"\s+"KP\d+"\s+[2-9]\b' 31168-A-FROM-DRAWINGS.e2k,31168-B-FROM-DRAWINGS.e2k,31168-C-FROM-DRAWINGS.e2k`. I expect nonzero matches in all three files, and specifically the measured counts from the brief: A 163, B 247, C 290. Then run `Select-String 'member-on-two-storeys' *-FROM-DRAWINGS-report.txt`; I expect no blocking violation because `ShippedModelInvariants.cs:381` only sees repeated assigns, while `ShippedModelInvariants.cs:49` drops the trailing span value from the parsed `LINE`.

### Building B still ships an empty 1.67 in storey, visibly preserving the sliver class of bug
SEVERITY  MATERIAL
WHERE     Kor.Operations.EngineeringTools.Core/Dxf/E2kDocument.cs:620
WHAT      The shared-base merge starts from this building's lowest tagged storey. For building B, `B-LEVEL 1` is the start, so `A-LEVEL 1` can be treated as shared base below it but `B-LEVEL 1` itself is retained as a separate 1.67 in storey. That refutes the suspected top-down `FirstOrDefault` cause: the list is explicitly ordered bottom-up at `E2kDocument.cs:603`.
TRIGGER   `31168-B-FROM-DRAWINGS.e2k`, where ground floor is drafted as `A-LEVEL 1` and `B-LEVEL 1` 1.67 in apart and the B run keeps its own first tagged storey.
CONSEQUENCE  Even if empty today, Andrea can open the storey list and see the same wafer-storey symptom we are saying the per-building split fixed. It is not structurally damaging today, but it is an avoidable credibility hit.
VERIFY    Run `Select-String 'STORY "B-LEVEL 1"|ASSIGN\s+"[^"]+"\s+"B-LEVEL 1"' 31168-B-FROM-DRAWINGS.e2k`. I expect exactly one `STORY "B-LEVEL 1"` line with height about `1.67`, and no `AREAASSIGN` or `LINEASSIGN` rows on `B-LEVEL 1`.

## Exactly One Thing To Fix Before She Opens It

Fix the member span behavior through real storeys. The blocking artifact is not the report, not the remaining empty B sliver, and not the wording around A/B/C. It is the fact that hundreds of columns still encode a two-storey span in per-building files and will still run through LEVEL 1 MEZZ. If those grep counts are nonzero, do not send the models.

## A/B/C Versus Combined

Ship A/B/C as the engineer deliverable. Retire the combined site model as a deliverable; keep it only as an internal composition artifact if the pipeline still needs it.

The honest wording to Andrea is:

> The combined site model forced all three buildings onto one interleaved storey list, which created artificial inch-high storeys and overlapping members. We split the deliverable into one model per building so walls and columns can be broken slab-to-slab on that building's real floor system. This supersedes the earlier "full model first, separate later" direction because that approach is what produced the overlap you flagged.

If the combined model stays for internal checks, the rule has to be: break at every real floor, span only across same-physical-level slivers. There should be one definition of sliver. Right now the code has both `SameFloorTolerance()` and `StoreysAtOneLevelGap = 12 in`, and those are not the same contract.

I would not rely on "one line object with one assign per storey gives one ETABS label full height" without a tiny ETABS import proof. The code comments assume that format behavior; the current files do not test it because they avoid multiple assigns and hide the span in the member connectivity.

## Input That Breaks Next

A job with a real populated mezzanine between two normal floors, drafted like 31168 LEVEL 1 / LEVEL 1 MEZZ / LEVEL 2, breaks this immediately. More specifically: any building where the main columns are drawn on the lower-floor sheet and the mezzanine has its own columns in the same plan area. The current span logic will run the lower-floor columns through the mezzanine while also placing mezzanine columns.
