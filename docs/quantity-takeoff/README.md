# Quantity Takeoff — input contract (Revit concrete schedule → CSV)

The takeoff reads a concrete-quantity schedule exported from Revit (or any spreadsheet saved
as CSV). No PDF extraction, no SAFE/ETABS involvement.

## Columns (case-insensitive, order-independent; "(m³)" style units are ignored)

| Column      | Required | Notes |
|-------------|----------|-------|
| `Level`     | yes      | e.g. `P3`, `L1` |
| `Element`   | no       | `Slab` \| `Wall` \| `Beam` \| `Column` \| `DropPanel` (default `Slab`) |
| `Grade`     | no       | e.g. `C30` |
| `ConcreteM3`| yes      | concrete volume in m³ — Revit gives this directly |
| `RebarKg`   | no       | if supplied, used as-is; if blank, estimated from element density |
| `FormworkM2`| no       | if supplied, used; else 0 |

Header aliases accepted: `Concrete`, `Volume`, `Concrete (m³)` all map to `ConcreteM3`;
`Category`/`Type` map to `Element`; etc.

## Workflow
1. In Revit, make a material-takeoff schedule of concrete by Level + Element with Volume (m³).
2. Export / copy it to a CSV matching the columns above (one file per issue).
3. In the app: import the IFT-stage CSV and the IFC-stage CSV, then Compare → export the
   client delta report (.docx / .xlsx).

Rebar with a blank cell is a density estimate (labelled as such); a supplied number is treated
as model/QS-sourced. See `sample-31065-IFT.csv` / `sample-31065-IFC.csv` for a worked example
(net +20 m³: transfer slab −20, plenum slab +40).
