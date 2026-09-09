# Codex 27 of N — a dimension string is a length the drafter wrote

## Goal

Type the dimension strings. The ledger counted 1,680 to 6,629 words per set as dimension strings
and read none; they are lengths, and between two grid axes a length is a claim the drawing makes
about itself that the axes' spacing at the sheet's scale can check.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## The class, in one sentence

A dimension string is a length the drafter wrote; between two grid axes it agrees with their
spacing at the sheet's scale or it is a member's length, and a bare number is a dimension only when
a span of axes agrees with it.

## Changed

- `Core/Intake/DimensionStrings.cs` (new): `Parse` (feet-inches, bare inches, bare millimetres);
  `Read(page, axes, mmPerPt, furniture)` — every string with its value, position, orientation (tall
  text is rotated), and the tightest span of axes whose gap agrees within 25.4 mm, else the
  adjacent pair it sits between.
- `SheetRecord.Dimensions`; `DrawingIntake` fills it after the grid axes exist; word fates: a
  parsed string is read with its value and its verdict; a bare number is read only when a span
  agrees.
- Ledger: "dimension strings typed with a value" with the agree / disagree counts and the first
  disagreements; "scale confirmed by the drawing's own grid dimensions" when three or more agree.
- Tests: `ADimensionStringIsALengthTheDraftsmanWroteTests`;
  `FiveStickFilesTests.DimensionStringsOnTheSchedulePageAreTheBankedCount`.

## Measured after

    set      typed   agree with a grid span   unread before -> after
    31130    4,001            0                26,242 -> 22,241
    31168    1,677            0                15,255 -> 13,578
    31138    5,858            0                27,336 -> 21,478
    31065      321            0                33,837 -> 33,521
    31202    6,626            0                31,195 -> 24,569

The premise was false on these sets: 0 of the strings on 294 pages agree with any span of grid
axes. KOR's structural plans dimension members and openings; the grid spacing is on the
architect's drawings. The check stays, with its zero, for a set that does dimension its grid.

## What remains

- A metric set's dimensions are bare numbers (31065: 128 on p14, none agreeing) and stay unread
  until a witness other than the grid exists: the dimension line, its extension lines and ticks.
- A typed value is not yet tied to the member beside it (a "22"" next to a wall is that wall's
  thickness).
