# CODEX-PUBLISH-TO-MASTER — "Publish to Master" in the governance app

## Goal
Add a **"Publish to Master"** action to the Standard Details governance module so an engineer,
after approving details, can make the **MASTER** template contain **only the approved details** —
by rebuilding it from the **AUTHORING** template through the KOR Drafter Bridge. This is the
completion of the approval loop the app already owns: approve → publish → the master shows it.

This is a **feature in the existing app**, NOT a standalone tool and NOT a new service. It lives
beside the promotion flow, which is already drained in-app.

## Where it goes (grounded — do not invent a new host)
- Module: `Kor.Operations.App/StandardDetails/`.
- The promotion outbox is already processed **in-app**, on demand:
  `StandardDetailsWindow.Logic.cs` reads pending rows → `KorStandardsPromoterRepository.PromoteAsync`
  → `StandardDetailsRepository.MarkOutboxDone/FailedAsync`. Put "Publish to Master" in the same
  window as a peer action — an async command with progress + a result summary, exactly like the
  promotion drain. No BackgroundService, no worker, no outbox rows (see "Why no outbox").
- The approved set is already read here: `KorStandardsReadRepository` (reads `detail.vw_PaletteCatalog`).
  Reuse it; add a method returning the approved `DetailNumber` set if one is not already present.

## Why no outbox / no service
A rebuild is **idempotent and atomic-per-run**: MASTER = AUTHORING minus the un-approved details,
recomputed from scratch each run. An interrupted or repeated run just rebuilds cleanly — so there
is nothing to queue or reconcile. The promotion outbox exists because promotions are many small
per-detail state changes; publish-to-master is ONE derive operation. Keep it a direct, re-runnable
in-app action.

## The model — DERIVE, never hand-edit
```
MASTER  =  AUTHORING  with every DETAIL view whose KOR-D is NOT approved deleted
```
Sheet composition is inherited from AUTHORING for free (approved details keep their place on the
S1.xx sheets; un-approved ones drop off with their view). Legends, schedules, general setup views
(no KOR-D prefix) are never touched.

## New code (the only genuinely new piece is the bridge client)
1. `DrafterBridgeClient` (in the StandardDetails module or a shared spot the module can reference):
   the C# equivalent of `Operations\docs\etabs-handoff\Send-Bridge.ps1` — write a command object as
   `<bridgeRoot>\inbox\<id>.json` (temp-name then rename), poll `<bridgeRoot>\outbox\<id>.json`,
   parse the envelope `{ ok, result, error, dialogs }`, timeout with a clear message. One method:
   `Task<BridgeReply> SendAsync(object command, TimeSpan timeout)`. Reuse the repo's existing JSON
   serializer — search first; do NOT add a new JSON dependency.
2. `MasterPublisher` — the orchestration (see algorithm). Takes the bridge client, the approved set,
   and config (authoring path, master path). Returns a structured result (removed KOR-D list, counts,
   verified bool).
3. A "Publish to Master" command in `StandardDetailsWindow` — async handler: confirm → run
   `MasterPublisher` with progress → show the result summary (removed N details, master now holds M).
   Gate it behind the same access policy the promotion actions use (`StandardDetailsAccessPolicy`).

## The bridge protocol (what `DrafterBridgeClient` speaks)
File-drop JSON queue. Reference: `C:\VIsual Studio Projects\KOR.Drafter\docs\PROTOCOL.md`.
`bridgeRoot` = `\\KOR-302N\C$\KOR.Drafter\bridge` (config). Verbs used, all PROVEN live 2026-09-02:
- `ping` — health check; if it fails, the workstation/Revit is down → surface an actionable error.
- `opendoc {path}` / `savedoc {path}` (Save-As to a new file) / `closedoc {doc}`.
- `query {kind:"views"}` → `[{id,name,type}]`.
- `getparams {ids:[...]}` → per element `parameters[]`, incl. `View Prefix` (the `KOR-D-#####`).
- `delete {ids:[...]}` — refuses if any id missing; reports `deletedTotal` (incl. cascades).
Coordinates in mm; names case-insensitive; `doc` (title substring) targets one of several open docs.
Envelope `ok` is true only on a real commit — but still re-query and assert (PROTOCOL "failure honesty").

## Algorithm (`MasterPublisher.PublishAsync`)
1. `ping`. Fail fast with a clear message if the bridge/Revit is not up.
2. `opendoc AuthoringPath`.
3. `savedoc MasterPath` (master = full copy of authoring; the active doc is now the master).
   Assert the active doc title is the master before any delete.
4. Approved set = `KorStandardsReadRepository` → distinct approved `DetailNumber` (IsPlaceable=1).
5. `query {kind:"views"}` on the master; `getparams` (batched, ~300/call) to read each view's
   `View Prefix`. A view is a DETAIL iff its prefix matches `^KOR-D-\d{5}$`.
6. Remove set = detail-view ids whose prefix is NOT in the approved set.
7. `delete {ids: removeSet}` (skip if empty).
8. `savedoc MasterPath`. Then **verify**: re-`query`+`getparams`, assert NO remaining view carries a
   non-approved KOR-D prefix. Assert on the fresh read, never on `ok` alone.
9. Return the result (removed KOR-D list; authoring/approved/master-after counts; verified=true).

## Hard constraints
- **NEVER write to AuthoringPath.** Only `savedoc` to MasterPath. Assert active-doc = master pre-delete.
- **Verify every commit on a fresh read.** No trust in `ok` alone.
- **Fail loud** — bridge down, save failed, or post-verify finds a stray non-approved detail →
  the action reports failure with the reason; the master is left as it was (the on-disk MASTER is only
  replaced by a successful savedoc, so a mid-run failure leaves the previous good master intact... 
  EXCEPT once step 3 overwrites it — so write to a temp master path, and only rename it over the live
  MASTER after step 8 verifies. That keeps a failed run from leaving a half-curated master live).
- **Idempotent** — two runs back-to-back: the second removes nothing.
- An approved KOR-D absent from AUTHORING is a warning (never drawn), not a failure — list it.

## Config (App.config, beside the existing KorStandards connection strings)
- `StandardDetails.AuthoringPath`  e.g. `C:\KOR.Drafter\tasks\template\AUTHORING\KOR-Standards-Authoring-R25.rvt`
- `StandardDetails.MasterPath`     e.g. `C:\KOR.Drafter\tasks\template\MASTER\KOR-Standards-Master-R25.rvt`
- `StandardDetails.BridgeRoot`     e.g. `\\KOR-302N\C$\KOR.Drafter\bridge`

## Out of scope (do NOT build)
- Any always-on service, worker, BackgroundService, or outbox rows for this (it is a direct action).
- copyview/placeview promotion (delete-from-a-copy is the model). Fleet deploy. Metric/imperial split.
- Any change to the bridge, the DB schema, or AUTHORING. This reads SQL + drives the existing bridge.

## Proven (build on these, don't re-litigate) — all exercised live on the real 120MB template, 2026-09-02
savedoc (Save-As, original untouched), query views, getparams `View Prefix`, delete (cascades reported),
copyview (lossless — kept as a fallback, not needed here). The bridge is UP only while Revit is open
on the workstation; the `ping`-first check handles that.
