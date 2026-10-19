# Engineering fix A3b response

- `PdfOnlyBuild.WriteSheets` now applies the plan-sheet gate only when `!markup`.
  The added comment cites Ian's 2026-10-19 decision. Nothing else in the method changed.
- `TheNewEntryRetainsTheOldGeometryAndDxfBytes` adds markup text to the old reader's
  expected geometry only when `markupOnly`, using scale 96 × points-to-mm.
  Its name and all assertions, including the DXF byte comparison, are unchanged.
- The App/CLI differential test was not modified.

Only the two specified code files and this response were changed in this follow-up.
No build or test was run; verification remains with Claude.
