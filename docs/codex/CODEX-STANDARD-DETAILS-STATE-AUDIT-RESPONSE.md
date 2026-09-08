# Standard Details audit — recovered findings

Recovered on 2026-09-07 from closed Codex session `01a07db6-cc5f-7530-8048-e9e71cb9f817`, titled **Run details state audit**. The original report was never written. This document salvages supported findings; it does **not** claim to complete the original 27-claim audit.

The original transcript survives at `C:/Users/ilalonde/.codex/sessions/2026/09/07/rollout-2026-09-07T14-12-07-01a07db6-cc5f-7530-8048-e9e71cb9f817.jsonl`. Its recorded activity spans approximately 14:12–16:15 Pacific. Selected original commands and their captured output are preserved in [the recovery evidence](CODEX-STANDARD-DETAILS-STATE-AUDIT-RECOVERED-EVIDENCE.md). References such as **L053** identify physical lines in that transcript, not source-code lines.

Database and remote-machine observations below are historical observations from that session. Recovery did not rerun their queries or remote scans. The PDF matching and publish sequencing findings were also checked against the current local source. No builds, tests, code fixes, database changes, or deployments were performed during recovery.

## Findings worth keeping

### 1. Critical: one successful export can be stored against other details

**Recommended single ship-blocker: incorrect PDF-to-detail matching.**

In [MasterPublisher.cs](../../Kor.Operations.App/StandardDetails/MasterPublisher.cs), `TryFindViewExportItem` at line 642 attempts to match a returned item to the requested detail. At line 660, if the returned collection contains exactly one item, it accepts that item even when its identity does not match the target.

The bridge's `ExportViews` implementation returns successful exports in `views` and failures separately in `failures`. Therefore a request for details A, B, and C can return just A in `views`. The publisher can accept A's PDF for B and C as well, then call `SetRenderedPdfAsync` under each target's detail number at line 281. The `batch.Count == 1` guard at line 270 applies to a different, top-level PDF fallback and does not protect this branch.

**Evidence:** L102 records the publisher source; L328 records bridge source lines 2207–2350, including `views = exported` and `failures = failures`. Recovery confirmed the current publisher still has the unconditional `count == 1` fallback.

**Limit:** this establishes a defective source path, not that stored drawings have already been corrupted. A focused reproduction should return one success and two failures for a three-detail batch and assert that only the successful detail receives PDF bytes.

### 2. Material: capture failures do not prevent MASTER replacement

`CapturePublishedPdfsAsync` converts target-loading and export errors into a result containing failures. Its caller at lines 168–188 still calls `ReplaceMaster` and returns `Verified: true` without checking those failures. Successful PDF writes also occur before replacement, so the database capture and model replacement do not form an atomic operation.

**Evidence:** L102, especially source lines 168–188, 202–226, and 240–298; checked again in current source during recovery.

**Limit:** `Verified` may describe model-content verification; it does not establish successful PDF capture. Reproduce a capture error with an otherwise valid model and observe the replacement and returned capture result before deciding the desired failure policy.

### 3. Material: the fleet publisher omits dependencies used by the pilot

The captured `KOR.RevitTools/build/publish.ps1` copies `KOR.RevitTools.dll`, `KOR.RevitTools.Core.dll`, symbols, and loader files. It does not copy the SQL client and native SNI dependency files in that publish sequence. The recorded repository history includes `d1d56e2`, “Ship the real SqlClient impl + native SNI, not the throwing facade.”

**Evidence:** L182 contains the full captured publisher and related source; L245 and L360 preserve dependency-related searches and observations.

**Limit:** these are source and artifact observations. They are not a clean-machine runtime test of the fleet package. Verify the resulting package's managed and native dependencies before treating pilot operation as evidence of fleet readiness.

### 4. The claim that the approval loop “never ran” is overstated

The original SQL output shows zero retained rows in the seven listed governance tables and no `human-confirmed` detail rows. Those are current-state counts, not an execution history.

The session also read `REVERT-demo-detail.sql`, whose comment says it restores a detail after a live walkthrough promoted it, and whose statements remove matching history and reset confidence. `TaskD-Promotion-LiveProof.sql` describes a promotion test that removes its own detail and history afterward. Their existence demonstrates why empty retained state cannot prove non-execution.

**Evidence:** L053 for the SQL results; L283 for the scripts read from disk.

**Limit:** script text and comments alone do not prove those scripts actually executed. The defensible verdict is **OVERSTATED**, not a claim that this recovery independently proved a successful historical approval.

### 5. Two concrete review corrections survived

- **The Desktop register is stale relative to the captured database state.** The September 3 workbook still lists `KOR-D-00003` as “Approved.” The SQL output records that detail as retired at `2026-09-04T05:15:56.2784207`. Evidence: L283 and L053. Whether that exact workbook was delivered to Jim was not independently established during recovery.
- **KOR.RevitTools had two commits beyond `backup/main`.** L418 lists `a6c8e38` and `d1d56e2`; L182 shows `backup/main` at `ab0fd9c`. This contradicts the blanket “nothing unpushed” claim relative to the recorded backup ref. Recovery did not contact or refresh the remote.

## Recovered database snapshot

These values come from complete aggregate result sets in L053, whose exact SELECT commands and output are retained in the evidence file.

| Observation | Captured result |
|---|---|
| Active / retired details | 608 / 4 |
| Active content-verified / unverified | 603 / 5 |
| Placeable palette details / view rows | 603 / 1,049 |
| Unplaceable palette details / view rows | 5 / 26 |
| Detail art rows with both PNG and PDF | 604 |
| Component art rows with PNG / PDF | 287 / 0 |
| Placeable details missing art | 0 |
| Detail occurrences | 1,079; all in the recorded template name; latest observation August 6 |
| Quick Insert placeable entries | 288 |
| Documents, DocumentVariants, DocumentVersions, FileBlobs, ApprovalRecords, PublicationRecords, StandardDetailPromotionOutbox | 0 each |
| AuditEvents | 43 |

The five active unverified details were `00100`, `00133`, `00173`, `00501`, and `00526`, each with `VariantsDiverge=true`. The full discipline, kind, timestamp, and promoter-permission results also survive in L053.

## Work attribution and unfinished scope

The old session's starting `git status` (L016) already contained the architecture changes, `MarkRowScheduleReader.cs` changes, architecture tests, TRX files, and quantity-takeoff brief/script. Its later status (L418) contains the same paths. They should not be attributed to work produced by this audit or removed as audit debris.

The full numbered claim ledger was never delivered. Revit runtime behavior, actual stored-PDF correctness, complete workstation discovery, historical script execution, and every remaining claim in the brief are not certified by this recovery. The evidence companion preserves all 83 recorded CommandExecution events, including captured output, failures, and original exit codes. Original truncation remains where present. [The recovered messages](CODEX-STANDARD-DETAILS-STATE-AUDIT-RECOVERED-MESSAGES.md) preserve 18 user and assistant messages, excluding the injected workspace instructions. No extracted command was rerun.

The recovery's deliverables are this report, the evidence companion, and the recovered messages. Only these Markdown documents were created by the recovery; no application code was changed.
