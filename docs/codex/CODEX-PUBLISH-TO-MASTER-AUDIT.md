# CODEX-PUBLISH-TO-MASTER-AUDIT — adversarial review

## Mandate
Adversarially audit the "Publish to Master" feature just implemented. Your job is to **break it**,
not confirm it. Assume the happy path works (it builds, and a normal run curates the master). Hunt
the edges where it would **corrupt the master, wipe data, silently ship a wrong result, or fail
unsafely**. Fix every CONFIRMED defect with the smallest change that holds; report each with the
concrete failure scenario. Do NOT build or run (no-build rule). Do NOT refactor working code, add
dependencies, or change the bridge/DB/AUTHORING. Keep the model: MASTER = AUTHORING minus un-approved.

## Files in scope
- `Kor.Operations.App/StandardDetails/MasterPublisher.cs`  (orchestration — primary target)
- `Kor.Operations.App/StandardDetails/DrafterBridgeClient.cs`  (bridge transport)
- `Kor.Operations.App/StandardDetails/KorStandardsReadRepository.cs`  (`LoadApprovedDetailNumbersAsync`)
- `Kor.Operations.App/StandardDetails/StandardDetailsWindow.{xaml,xaml.cs,Logic.cs}`  (wiring/gate)
- `Kor.Operations.App/App.config` + config plumbing (AppConfigKeys/StorageOptions/CompositionHelpers)

## Already verified — do NOT re-litigate (spend your effort elsewhere)
- Solution builds, 0 errors. Approved set is `IReadOnlySet<string>` (OrdinalIgnoreCase).
- Bridge verbs proven live: `ping/opendoc/savedoc/query{views}/getparams/delete/closedoc`.
- `getparams` returns each view's prefix as `parameters[] { "name":"View Prefix","storage":"String","value":"KOR-D-#####" }` — the parser reads `value`. Confirmed against the live 120 MB template.
- Save-As writes a NEW file; the source (authoring) is not modified by savedoc-to-temp.

## The attacks to run (find MORE than these)
1. **⛔ EMPTY / degenerate approved set wipes the master.** If `LoadApprovedDetailNumbersAsync`
   returns 0 rows (view broken, everything rejected, wrong DB, migration half-run) — but the SQL
   connection itself succeeds — then EVERY detail view is "not approved" and gets deleted, producing
   an **empty master that passes verification and reports success.** This is catastrophic data
   presentation loss. **Fix: refuse to publish when the approved set is empty** (and consider a
   sanity floor / a "removing more than N% of details" guardrail that makes the caller confirm).
   The tool must never turn a healthy master into an empty one because SQL hiccuped.
2. **Silent wrong result.** Any path where the master ends up NOT approved-only yet the run reports
   success: getparams reply-shape variance, a detail with multiple views where only some ids are
   collected, a view whose `View Prefix` is blank/omitted (treated as non-detail → kept) that IS
   actually a detail, id/prefix case mismatches. Verify the verify step truly re-reads and can fail.
3. **AUTHORING integrity.** Prove no path can savedoc/overwrite the authoring file. Check that after
   `opendoc authoring → savedoc temp`, every later `savedoc`/`delete`/`query` acts on the TEMP doc,
   not authoring. **Add an explicit active-doc assertion (via ping/activeDoc) that the active doc is
   the temp master BEFORE the delete** — do not rely only on savedoc throwing.
4. **Live-master safety across failure.** Trace every throw between step 3 and the final
   `ReplaceMaster`. Confirm a failure ANYWHERE leaves the live master byte-unchanged. Confirm
   `ReplaceMaster`/`File.Replace` over a UNC `\\host\C$` share behaves (same-volume requirement); if
   it can fail or leave a gap where the master is momentarily absent, make the swap atomic-or-safe.
5. **Orphaned temp files.** On EVERY failure path the `.publishing.*.rvt` temp (120 MB) and its open
   Revit doc must be cleaned up (delete the file; `closedoc` the temp in Revit). Today there is no
   cleanup on failure — fix it (try/finally), best-effort, without masking the original error.
6. **The delete targets the active doc (no `doc` field).** Confirm the active doc is still the temp
   master at delete time (queries/getparams between savedoc and delete must not have switched it), or
   pass `doc` explicitly. A delete against the wrong open doc is unacceptable.
7. **Temp-doc release before replace.** `ReleaseTempDocumentAsync` opens authoring then `closedoc`s
   the temp — confirm (a) reopening authoring cannot re-save/modify it, (b) the temp is reliably
   unlocked before `File.Replace`, (c) if the LIVE master happens to be open in that Revit, replace
   won't silently corrupt or throw unhandled.
8. **Concurrency / re-entry.** Button-disable is UI-only. Two app instances, or a second click via a
   different path, or the bridge already mid-command — reason about interleaving on the single-threaded
   bridge and the shared temp/live files. At minimum, make double-publish safe (unique temp already
   helps; ensure the final replace can't clobber a concurrent run's good master).
9. **getparams batching (300).** Off-by-one / dropped ids / a batch that returns fewer elements than
   requested (a deleted or non-gettable id) — ensure no view is silently mis-classified as "no prefix".
10. **Config / path edges.** `ResolveControllerFilePath` derives the UNC from `BridgeRoot`'s host+drive
    and assumes the master path is on that same drive. Non-C: paths, trailing slashes, a BridgeRoot
    that isn't a `\\host\X$` share, mixed-case drive — confirm it degrades to a clear error, never a
    wrong path that writes somewhere unexpected.

## Output
For each CONFIRMED defect: the file+line, the failure scenario (concrete inputs → wrong outcome), and
the minimal fix applied. Rank by severity (data loss > silent-wrong > unsafe-fail > cosmetic). If an
item on the list turns out to be a non-issue, say why in one line and move on.
