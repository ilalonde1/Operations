# BdDocTemplate — the canonical KOR document design system

This is THE look for every KOR BD document: dossiers, handbooks, briefs, playbooks.
It is the design Ian pointed at ("like KOR-BD-QuickStart.pdf and KOR-BD-Handbook.pdf")
— KOR slate `#3F5364` + orange `#FF5B35`, warm paper ground, gradient hero with the
accent rule, mono eyebrows/kickers, diamond bullets, status pills, callout boxes
(`box--screen/tip/dont/auto`), flow steps, editorial tables, and a print block that
forces the light palette and Letter-page margins.

## How to produce a document
1. Copy the `<style>` block verbatim from `reference-handbook.html` (do not restyle
   per-document; the system IS the brand). Add at most a small page-specific rule
   (e.g. a single-column `.doc` wrapper when no table of contents is needed).
2. Compose the body from the existing components — hero, `kicker`+`h2` sections,
   `ul.plain`, `map` definition grids, `table-wrap` tables, `pill` statuses,
   `box` callouts, `flow` steps, `footer`.
3. Render the PDF: `tools\Format-BdWebPdf.ps1 -Html <page.html> -Pdf docs\<Name>-web.pdf`
   (headless Edge; the `@media print` block in the template does the rest).
4. Optionally publish the same HTML as a Claude artifact for a live page.

Reference implementations: `reference-handbook.html` (long-form with sticky numbered
TOC), `reference-quickstart.html` (one-pager), and the RWA dossier
(`docs/KOR-RWA-Dossier-2026-07-03-web.pdf`) as the first dossier on the system.

## App integration (the end state)
The WPF app's `BriefPdfGenerator` (QuestPDF) should be superseded by an HTML brief
renderer that fills this template from the existing Brief data models and prints via
headless Edge (same mechanism as `Format-BdWebPdf.ps1`), so app-generated briefs are
pixel-identical to these documents. Until that ships, the QuestPDF path carries an
approximation of this language (commit 456833b5).
