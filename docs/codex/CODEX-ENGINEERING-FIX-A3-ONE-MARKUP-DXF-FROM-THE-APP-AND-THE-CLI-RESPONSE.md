# Engineering fix A3 response

- Moved the markup text reader and its private width helper unchanged into Core's
  `PdfToSafe/MarkupText.cs`; the App parser now forwards to it.
  A static comparison confirmed the moved block matches the original exactly.
- `DrawingIntake.ReadPage` adds markup labels after classification, only for
  classified markup reads, using `scaleFactor` (denominator × points-to-mm).
  Page-content mode and classification rules are unchanged.
- Added CLI `--layer-by-colour`, documented it in usage, and reject its combination
  with `--kor-layers` with one error line and exit code 1.
- Both `PdfOnlyBuild.WriteSheets` export paths receive the colour choice.
  Its optional argument defaults to false and is appended to preserve positional callers.
- Added “Layers by markup colour” beside Export DXF. Its binding reads and saves
  the existing firm-defaults instance through `DefaultDxfLayerByColour` (default false).
  DXF export captures that choice before starting its background work.
- Authored `TheMarkupDxfIsTheSameFromTheAppAndTheCliTests`: the Regent fixture,
  page 1 at scale 96, both layer choices, nonempty App shapes and labels, and
  POLYLINE/TEXT multiset comparisons at 0.1 mm with counts printed.
  Remarks exclude scale detection, page-content mode and grid colour settings.
- Authored Core `MarkupColoursRemainDxfLayersTests`: two colours retain their
  declared and assigned colour layers without SLAB, BEAM or COLUMN layers.

The user approved reading the missing geometry-creation and firm-defaults wiring
sections in the two already named files. No git or repository search was run.
No build, restore or test was run; the gates await Claude's execution verification.
GeometryFilterService, ReclassifyByColor and Baselines were not modified.
