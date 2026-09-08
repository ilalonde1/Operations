# Codex 12 of N — a table is bounded by its own border, not by a fraction of the page

## Goal

Make `MarkRowScheduleReader` attribute rows to a schedule by the border the schedule draws,
and restore three footing takeoffs that Codex 11's landing silently broke while stating they were
unchanged. Same class as every intake defect so far — a convention compiled into a binary — but
this instance cost real numbers on shipped verbs, and the check that would have caught it did not
exist. It exists now, and it is red.

## The regression, measured with a differential

`takeoff footings` on the same five local stick files, from builds of two commits:

    commit     31130-01              31138-01               31065-01                    31168-01
    4fd1abbe   258 cy  F1-F4,SF1     353 cy  F1,F2,SF1,SF2  1,174 cy  F1-F4,SF1,SF2     0 cy
    8300439d    48 cy  0",15M,20M    353 cy  F1,F2,SF1        855 cy  F1-F4,SF1,SF2     0 cy

HEAD runs the same reader code as `8300439d` (no reader file changed since), so this is what ships
today. `8300439d`'s message says "Footings unchanged: 31065 1,174 cy, 31138 353 cy, 31130 258 cy".
Two of the three were wrong when written; the third matched only because a strip footing that
was lost (31138 SF2) is priced as "length on plan, residual" and does not move the total.

Also visible on the column side, from `pdf-takeoff`'s self-check at HEAD: 31065 p14 reports
`unplaced 8-35M, BOT.` and covers **0 of 15** labelled columns; 31168 p11–13 list a bare `C` as an
unplaced mark.

## The class, in one sentence

`ReadFromScheduleColumns` bounds a schedule's rows by a window of ±18% of the page width around
its heading and by 6-point y-buckets, and by nothing the table itself draws — so any token that
shares a y-bucket inside that window becomes the leftmost "mark" of a row whose cells are then
read from a different table, or from the second line of a wrapped cell.

Two shapes, both seen:

- **A neighbouring table's wrapped cell.** 31130 p11: FOUNDATION SCHEDULE at x=1665 is 396 pt
  wide; its window is ±544 pt, which covers the SHEAR WALL SCHEDULE at x=1278–1602 to its left.
  That table's REINFORCING cell is two lines (`15M @ 14" VERT. EACH FACE` / `15M @ 14" HORIZ.`)
  while its mark `SWA` is centred between them, so the y-bucket of each line has `15M` as its
  leftmost token, the row text runs right into the foundation table's `4'-0" x 4'-0" x 26" DEEP`,
  and the reader emits **mark 15M, size 1219x1219x660**, then counts 14 placements of `15M` on
  the plan — rebar callouts.
- **The same table's wrapped cell.** 31065 p14 PARKADE COLUMN SCHEDULE: `PC4`'s REINFORCING cell
  is `8-35M VERT'S.` over `10M @ 150 TIES`; each line is its own y-bucket, `8-35M` is leftmost in
  its bucket, and it becomes a mark. The anchor rule (`MarkColumnTolerancePts` 20 around the
  topmost candidate's left edge) then keeps or drops REAL rows depending on which token happened
  to be topmost — which is how 31065 p14 lost `F1` and 31138 lost `SF2`.

## Measured premise: the tables draw their own extent

Every schedule title on every page of the five jobs (294 pages, 311 titles at title size), with a
detector that takes the topmost horizontal rule under the title spanning its x, the verticals
that meet it, and the lowest end of those verticals:

    kind                  titles   bordered   the rest
    FOUNDATION SCHEDULE       12         12   —
    COLUMN SCHEDULE           67         55   4 are sheet names in title blocks (S4.01 etc.),
                                              8 are 31202's, ruled but drawn cell by cell
    SHEAR WALL SCHEDULE       42         34   8 are sheet names in title blocks

So on every job but 31202, every real table the readers target has a drawn border, and 31130 p12's
"108 horizontal / 80 vertical" was not a one-off. 31202's column schedule is a different
convention altogether: ruled, but with **no mark column** — rows are keyed by size (`14" x 48"`)
— and its foundations are a raft (`RAFT SLAB REINFORCING SCHEDULE`, p13), so its 0 footings is
correct and should be reported as "raft slab", not as silence.

Rendered crops of every table above are what these numbers were checked against; the mechanism
is in the pictures, not inferred.

## Change this — the extent comes from the linework

1. **Find the table's border from the title.** Topmost horizontal segment under the title that
   spans the title's x; the verticals that meet it; the lowest end of those verticals is the
   bottom. Both `l` and `re` path items count; a rule drawn as several collinear pieces (31202)
   is one rule when the pieces abut within a point. `VectorPageReader` already yields the paths.
2. **Rows are the cells between the table's horizontal rules**, not 6-point y-buckets. A wrapped
   cell has two lines and one rule above and below; both lines belong to that row, and the mark
   is the leftmost cell of the row, whatever its line count.
3. **Nothing outside the border joins a row.** A token from a neighbouring table cannot be the
   leftmost anything, and `RowWidthPts` stops mattering because the row ends at the right rule.
4. **The band stays as the fallback**, for a heading with no border under it, and `Row.Route`
   says which extent was used, exactly as `MarkRoute` already says which mark route was. A reader
   that silently changes how it bounded a table is the same class of fault as this file is about.
5. **The anchor rule goes**, or becomes an assertion: once rows come from cells, every mark is in
   the first column by construction, and a topmost-candidate anchor that can drop `F1` is a
   liability with nothing left to do.
6. **Say when a schedule has no mark column** (31202) rather than returning nothing, and let
   `FootingScheduleReader` report a raft slab when the page has that title and no footing one.

## What NOT to do

- **Do not fix it by tightening the band.** 0.18 → 0.10 makes 31130 pass and leaves 31065's
  wrapped cells exactly where they are; it is the third heuristic for this after a point scope and
  a nearest heading, and the seed already says all three have been right on some sheets and wrong
  on others.
- **Do not bring back the mark regex as the gate.** Reading marks literally is right and stays.
  The regex is the shape of a mark; the border is the extent of a table; they answer different
  questions.
- **Do not touch the extraction layer, the DXF classifier's own rules, or `PrintedLength`.**
- **Do not rewrite the readers.** They read four practices' layouts; make the extent come from the
  border and leave the cell parsing alone.

## What I will check — and what is already checkable

`tools/Measure-StickFileSchedules.ps1` is the one command that measures every deliverable this
touches: footing totals and mark sets on all five jobs, and the column self-check per schedule
page. It is **red at HEAD** on the footing totals of 31130 and 31065, the mark sets of 31130 and
31138, and the unplaced lists of 31065 p14 and 31168 p11–13. It states what it covers and does not.

    footings   31130 258 cy F1,F2,F3,F4,SF1 · 31138 353 cy F1,F2,SF1,SF2 · 31065 1,174 cy F1–F4,SF1,SF2
               31168 0 (rows still do not parse — separate cause) · 31202 0, reported as raft slab
    columns    marks 31130 7 · 31138 10 · 31065 7 · 31168 more than 6, none of them 8-35M, BOT. or C
    coverage   no page goes down; 31065 p14 rises from 0/15
    route      every Row says border or band; on these five jobs every FOUNDATION and COLUMN row
               that reads today says border, except 31202's

Codex cannot run the stick files; Ian runs the script after landing, and its output goes in the
commit body as X of Y. A unit test on a synthetic page with two abutting tables and a wrapped
cell belongs in the tests project and should fail on the current reader before the change.
