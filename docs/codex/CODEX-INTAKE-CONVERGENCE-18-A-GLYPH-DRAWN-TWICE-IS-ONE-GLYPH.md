# Codex 18 of N — a glyph drawn twice at one place is one glyph

## Goal

Stop the word extractor from interleaving the two copies of a double-drawn letter. KOR's title
blocks set their 8 pt labels and 8.4 pt notes in fake bold — every glyph drawn twice at the same
origin — and `VectorPageReader` hands PdfPig's nearest-neighbour extractor both copies, which
orders them by position and, the positions being equal, arbitrarily: PROJECT TITLE arrives as
"PRPORJOEJCETC T TITTILTEL E" and SHEET TITLE never exists as a token. Everything that reads the
title block by its labels is blind on exactly the sets it was built for.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## Measured (fitz, raw glyphs, five plan pages)

    page          letters   drawn twice   offset       sizes affected
    31138 p9        4,805      69 (1%)    (0.0, 0.0)   8.1, 9.6
    31130 p11       5,579     358 (6%)    (0.0, 0.0)   8.1, 8.4, 9.6, 10.5
    31168 p14       5,662      69 (1%)    (0.0, 0.0)   8.1, 9.6
    31065 p14       8,225      86 (1%)    (0.0, 0.0) mostly; a few at (0.1, 0.2)
    31202 p17       5,990       0

The offset is zero: the second copy sits exactly on the first. The affected sizes are the title
block's labels (8.1), its field values (9.6) and the small notes (8.4). Titles (16.4) and marks
are drawn once.

## The class, in one sentence

Two identical glyphs at one origin are one glyph drawn boldly, and a reader that keeps both cannot
form the word.

## Change this

`VectorPageReader.ReadPage`: before word extraction, drop every letter whose value, font size and
origin (to 0.1 pt) equal an earlier letter's; then `NearestNeighbourWordExtractor.Instance.GetWords(letters)`
instead of `page.GetWords(...)`. Nothing else. `PdfWordDedupe` (word-level, for the readers that use
PdfPig's default splitter) stays as it is; it removes clean duplicate WORDS and cannot undo an
interleave.

## What I will check

- `TitleBlockFields.Read` returns SHEET TITLE, SHEET NUMBER, SCALE, PROJECT NO, DRAWN BY, CHK'D BY
  on 31130, 31138 and 31168 pages; `sched-border` prints them.
- The typing check against bookmark titles: 31130 and 31168 rise from 55 of 60 and 27 of 41
  towards 31065's 73 of 73, because the SHEET TITLE field now types them.
- `FiveStickFilesTests` values unchanged or explained; fast suite green; the thirteen DXFs against
  step 3 — a change there is the furniture rule seeing a title it could not read before, and is
  looked at, not accepted.
- The ledger: word counts fall by the duplicates removed; the totals fall by the same; nothing
  else moves.
