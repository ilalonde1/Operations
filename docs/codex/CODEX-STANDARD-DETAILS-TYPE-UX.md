# CODEX — Standard Details: make Type read as classification (not part of Approve) + confirm its save

## Goal

Users think the **Type** dropdown is tied to the **Approve** button because they sit together, and
they can't tell that Type saves on its own. Fix both:

1. **Separate them visually** — move the Type control out of the Approve/Reject cluster, up into the
   detail header near the title/status, so it reads as "what this detail *is*," not part of the
   approve action.
2. **Confirm the save** — Type already persists the instant it changes (`DetailTypeCombo_SelectionChanged`
   → `SetDetailTypeAsync`); show a clear inline **"✓ Saved"** next to the dropdown when that succeeds,
   so there's no doubt it took effect without clicking Approve.

Do NOT change the save logic, the tab-hop on sheet-ness change, or Approve/Reject — this is layout +
feedback only.

## Why / ground truth (do not relitigate)

- Detail pane grid (`StandardDetailsWindow.xaml`, the right `Border`, ~line 378): Row 0 = title +
  subtitle + `DetailStatusPill`; Row 1 = drawing; Row 2 = footer. The footer's Row 0 is
  `DetailTypePanel` (the **Type** label + `DetailTypeCombo` + the **Open PDF** button); the footer's
  Row 1 is `ActionHintText` + the Reject/Approve buttons. So Type currently sits right beside the
  gate — that's the confusion.
- `DetailTypeCombo_SelectionChanged` (Logic.cs ~847) saves and calls `SetActivityMessage(message,
  BannerTone.Success)` on success. `SetDetailTypeControl(detail, visible)` (Logic.cs ~1279) sets the
  combo when a new row is selected (guarded by `_syncingTypeUi`).

## What to build

1. **Move the Type control into the header (Row 0), labeled "Change Type".** Put a **"Change Type"**
   label + `DetailTypeCombo` under the title/subtitle (or aligned with the status pill), so it lives
   with the detail's identity and reads as an action you take (not part of Approve). Keep the same
   combo, values, and `SelectionChanged` handler — just relocate it and relabel from "Type" to
   "Change Type".
2. **Add "✓ Saved" feedback.** Next to the relocated dropdown, add a small, initially-hidden
   `TextBlock` (e.g. green "✓ Saved"). Show it when `DetailTypeCombo_SelectionChanged` persists
   successfully; **hide it** at the start of a change and whenever `SetDetailTypeControl` runs for a
   newly-selected detail (so it never lingers on a different item). A brief auto-fade is optional; the
   important thing is it clearly appears on save and doesn't stick around on the wrong detail.
3. **Open PDF becomes its own action.** It's a *view* action, not classification — move
   `OpenSheetPdfButton` to the footer's action area, left-aligned and clearly separate from
   Reject/Approve (which stay right). Its handler, busy state, and enable logic are unchanged.
4. Update the footer hint text if it referenced Type, so the footer reads as "open / approve /
   reject" only.

## Constraints
- UI-only. No change to `SetDetailTypeAsync`, `DetailTypeCombo_SelectionChanged`'s save path,
  `OpenDetailPdfAsync`, or Approve/Reject. The Type↔(Kind,IsSheet) mapping and tab-hop stay as they
  are.
- Reuse existing styles; keep the pane responsive at the current window size. Build gate: warnings
  are errors; no new warnings. No build/test steps.

## Verification (done by the requester)
The Type control now sits by the title, visually apart from Approve/Reject. Changing it shows
"✓ Saved" immediately (no Approve needed); selecting a different detail clears that confirmation and
shows the new detail's current Type. Open PDF sits on its own, apart from the gate, and still works
(static). Approve/Reject behave exactly as before.
