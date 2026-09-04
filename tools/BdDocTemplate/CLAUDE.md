# Dossier template — working rules

Loads on top of the root `CLAUDE.md`. This directory is the canonical design system for every KOR
dossier. `reference-handbook.html` is the full multi-page shape; `reference-quickstart.html` is the
short card. **Copy the `<style>` block verbatim into a new document** — do not invent a per-subject
palette.

## How a dossier is built and shipped

1. Write the body HTML using the template's classes (`hero`, `shell`, `toc`, `kicker`, `lede`,
   `box box--tip|dont|screen|auto`, `flow`, `pill`, `table-wrap`).
2. Assemble: template `<style>` + your body.
3. Render: `tools/Format-BdWebPdf.ps1 -Html <page>.html -Pdf docs/<Name>-web.pdf`.
   Naming is always the `-web.pdf` suffix.
4. **Verify the PDF, never the HTML.**

## Verifying — the part that keeps being skipped

- `pdftotext -layout` the shipped PDF and grep for the facts you believe you wrote.
- **`pdftoppm -png -r 70 -f 1 -l 2` and LOOK at the pages.** Page count alone will not tell you a
  hero is still inset or a table lost two columns. Both have shipped that way.
- Confirm `Producer: Skia/PDF` — if it says QuestPDF or the file is stale, headless Edge handed off
  to a running Edge instance and never wrote. Edge also caches `file://`, so render a fresh copy.

⚠ **Wide tables silently lose their right-hand columns.** A three- or four-column table with prose
in the last cell overflows and the extra columns are simply gone from the PDF — no error. Both a
Codex findings table and this template's own discipline tables shipped clipped. **For anything with
long text per row, use `<ul class="plain">` with a bolded lead-in instead of a table.** Keep tables
for short, tabular values.

## Print CSS — settled 2026-09-03, do not relitigate

```css
@page { size: Letter; margin: 11mm 0 13mm; }   /* sides ZERO, top/bottom real */
.hero  { margin: -11mm 0 0; padding: 13mm 13mm 10mm; }
.shell { padding: 0 13mm; }
:root  { --maxread: 100%; }
.toc   { break-after: auto; }
```

Why each line exists:

- **Zero side margins** let the hero bleed to the paper edge. A negative margin cannot escape the
  `@page` box in Edge — that was tried and rendered as an inset card. Top and bottom margins stay
  real because container padding does **not** repeat per page, so `@page{margin:0}` leaves pages
  2..n jammed against the edge.
- **`--maxread: 100%`** — 44rem is a screen reading measure; left alone it leaves a dead band down
  the right of every printed page.
- **`.toc { break-after: auto }`** — a forced break left two-thirds of page 1 blank.
- **`.hero strong { color: #FFF }`** must live outside `@media print`, or bold text in the hero
  renders navy-on-navy and is invisible.

Together these took a real dossier from **12 pages to 5 with larger type** (15px → 16.5px).

The quickstart card deliberately does **not** carry these — it has no hero, no contents and no
`--maxread` in print, so it has none of the defects.
