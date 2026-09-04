# CODEX — Standard Details: collapse Kind + Sheet into one "Type" dropdown

## Goal

The detail pane currently has TWO overlapping classification controls next to Approve/Reject — a
**Kind** dropdown (general-note / typical / custom) and a **Sheet** checkbox — and they confuse
users because "general note" and "Sheet" mean the same thing. Replace both with **one "Type"
dropdown**:

- **Typical detail** → Details tab
- **Custom detail** → Details tab
- **Note / schedule** → Sheets tab (this IS "a sheet")

No schema change. "Type" is just a single, non-redundant presentation of the existing `kind` +
`IsSheet` fields.

## Why / ground truth (do not relitigate)

- `detail.Detail.Kind` ∈ {general-note, typical, custom} and `detail.Detail.IsSheet` (bit) already
  exist and are seeded **consistently**: every general-note row has IsSheet=1; every typical/custom
  row has IsSheet=0. So Type maps 1:1 to (Kind, IsSheet) with no data migration.
- Writers exist: `detail.SetDetailKind` and `detail.SetDetailIsSheet` (both promoter EXECUTE),
  surfaced as `KorStandardsPromoterRepository.SetDetailKindAsync` and `SetDetailIsSheetAsync`.
- The pane currently binds a Kind dropdown → `SetDetailKindAsync` and a Sheet checkbox →
  `SetDetailIsSheetAsync`. The Details/Sheets tabs filter on `IsSheet` (0/1) — leave that as is.
- `vw_PaletteCatalog` already exposes `Kind` and `IsSheet`; `PaletteDetailRow`/`DocumentRow` carry
  both. No new columns, procs, or migration.

## The Type ↔ (Kind, IsSheet) mapping (the whole contract)

| Type dropdown value | Kind          | IsSheet |
|---------------------|---------------|---------|
| Typical detail      | typical       | 0       |
| Custom detail       | custom        | 0       |
| Note / schedule     | general-note  | 1       |

- **Read** (which value to show for a row): `IsSheet == 1` → "Note / schedule"; else `Kind ==
  custom` → "Custom detail"; else → "Typical detail".
- **Write** (on change): set BOTH fields to match the row above, in one action.

## What to build

1. **Remove** the Kind dropdown and the Sheet checkbox from the detail pane.
2. **Add one "Type" dropdown** in their place, populated with the three values, its selection
   derived from the row's (Kind, IsSheet) per the read rule.
3. On change, persist both fields consistently — add
   `KorStandardsPromoterRepository.SetDetailTypeAsync(detailNumber, type)` that calls the existing
   `SetDetailKindAsync` + `SetDetailIsSheetAsync` (or the two procs) for the mapped values, then
   refresh the row so it moves tab if its sheet-ness changed. Gate on the same approve/reject
   policy the current setters use. (No new DB proc.)
4. **Top-of-list filter**: rename the existing "All kinds" filter to **"All types"** with the same
   three values + All, mapping each to the (Kind, IsSheet) predicate the queries already accept
   (Note/schedule → IsSheet=1; Typical → Kind=typical & IsSheet=0; Custom → Kind=custom &
   IsSheet=0). If that's more than a trivial change, leave the top filter as-is for now and only fix
   the pane — the pane is the confusing part.

## Constraints
- No schema change, no migration, no new stored proc. Reuse Kind, IsSheet, and the two existing
  setters/procs.
- Keep the Details/Sheets/Parts tabs and their IsSheet filtering exactly as they are — Type only
  replaces the two per-item controls (and optionally the top filter).
- Reuse existing styles/DI and the approve/reject policy gating. Build gate: warnings are errors;
  no new warnings. No build/test steps.

## Verification (done by the requester, not Codex)
Open a detail: the pane shows ONE "Type" dropdown (no separate Kind/Sheet controls) reflecting the
row's current classification. Change it to "Note / schedule" → the row becomes a sheet and moves to
the Sheets tab; change back → returns to Details. Confirm Kind + IsSheet in the DB match the chosen
Type, and approve/reject + the drawing are unaffected.
