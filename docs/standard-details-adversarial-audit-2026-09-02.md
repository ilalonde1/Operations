# Standard Details Governance Bridge - Adversarial Audit

Date: 2026-09-02

Scope: WP1 and Tasks A-D for the Standard Details governance bridge across Operations/KorTransmittals and KOR.Drafter/KorStandards.

Constraints followed during audit: read-only inspection only. No build, no test run, no SQL execution, no git operation, and no source modification during the audit.

## Findings

1. CONFIRMED High: linked approvals can now fail hard if outbox enqueue fails.

`DecideAsync` puts the outbox insert inside the approval transaction, which is atomic, but there is no catch around `_repo.DecideAsync(...)` in the approve path. If `SELECT DetailNumber` or `INSERT INTO dbo.StandardDetailPromotionOutbox` throws because `005` is missing, permissions are wrong, or schema changed, the approval rolls back and the async event path can surface an unhandled exception instead of a friendly failure.

Evidence:
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:522`
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:573`
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:580`
- `Kor.Operations.App/StandardDetails/StandardDetailsWindow.Logic.cs:457`
- `Kor.Operations.App/StandardDetails/StandardDetailsWindow.Logic.cs:487`

2. CONFIRMED High: approved/enqueued documents cannot be deleted by the current app delete path.

`005` adds an FK from `StandardDetailPromotionOutbox.DocumentId` to `Documents.DocumentId`, but `DeleteRecordAsync` deletes approvals, publications, versions, then documents; it never deletes or clears outbox rows. Any document with an outbox row blocks `DELETE FROM dbo.Documents`.

Evidence:
- `db/KorTransmittals/005_PromotionOutbox.sql:50`
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:439`
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:442`

3. CONFIRMED High: concurrent queue processors can overwrite terminal state.

`LoadPendingOutboxAsync` reads `Status = 0`, but `MarkOutboxDoneAsync` and `MarkOutboxFailedAsync` update by id only, not `WHERE PromotionOutboxId=@id AND Status=0`. Two app instances can load the same pending row; one can mark done after successful promotion, then another can mark failed after a transient connection/proc error, leaving a successfully promoted detail shown as failed.

Evidence:
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:625`
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:646`
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:660`

4. CONFIRMED Medium: failed rows are not retryable from the queue despite the dossier saying they stay pending for retry.

The processor marks failures `Status=2`; `Process Pending` only loads `Status=0`. There is no retry-failed path. The dossier says the row "stays pending for retry from the Promotion Queue," which is false.

Evidence:
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:625`
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:651`
- `Kor.Operations.App/StandardDetails/PromotionQueueWindow.xaml:68`
- Extracted PDF text `KOR-StandardDetails-Governance-Module-2026-09-02-web.txt:115-116`

5. CONFIRMED Medium: "approval makes it placeable" is overstated.

KorStandards placeability requires `Confidence IN (...)` and `VariantsDiverge = 0`; the proc only updates `Confidence`. The picker shows `IsPlaceable` but does not expose or block `VariantsDiverge`, so approving a linked divergent detail can produce `human-confirmed` but still not placeable.

Evidence:
- `C:/VIsual Studio Projects/KOR.Drafter/db/023_StandardsReader.sql:25`
- `C:/VIsual Studio Projects/KOR.Drafter/db/023_StandardsReader.sql:28`
- `C:/VIsual Studio Projects/KOR.Drafter/db/004_CreateDetailAndConformance.sql:401`
- `C:/VIsual Studio Projects/KOR.Drafter/db/004_CreateDetailAndConformance.sql:402`
- `Kor.Operations.App/StandardDetails/KorStandardsReadRepository.cs:24`
- `Kor.Operations.App/StandardDetails/LinkDetailWindow.xaml:66`

6. CONFIRMED Medium: the SQL security assertion does not prove "EXECUTE and nothing else."

`066` checks direct object permissions in `sys.database_permissions`, but it does not check `sys.database_role_members`. A pre-existing `standards_promoter` user in `db_datareader`, `db_datawriter`, or another role would pass this assertion while having table access.

Evidence:
- `C:/VIsual Studio Projects/KOR.Drafter/db/066_PromoteDetailProcAndPromoter.sql:177`
- `C:/VIsual Studio Projects/KOR.Drafter/db/066_PromoteDetailProcAndPromoter.sql:195`

7. CONFIRMED Medium: purge script 004 does not identify the 12 known stranded records.

It guards only `Documents` count, version status, official flag, and default variants. If the database later contains exactly 12 legitimate draft documents, it will delete them. There is no guard on known ids, titles, creation dates, storage root, or blob paths.

Evidence:
- `db/KorTransmittals/004_PurgeStrandedTestRecords_APPLY.sql:32`
- `db/KorTransmittals/004_PurgeStrandedTestRecords_APPLY.sql:39`
- `db/KorTransmittals/004_PurgeStrandedTestRecords_APPLY.sql:70`

8. CONFIRMED Low: WP1 variant migration is not zero-footprint if any backfilled documents remain.

`002` creates `DocumentVariants` with an FK to `Documents` and backfills variants. The app delete path still does not delete variants, so backfilled documents cannot be deleted unless the purge has already removed them.

Evidence:
- `db/KorTransmittals/002_DocumentVariantsAndDetailNumber.sql:53`
- `db/KorTransmittals/002_DocumentVariantsAndDetailNumber.sql:110`
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:439`
- `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:442`

## Checked And Mostly Sound

The app does not directly write KorStandards tables; promoter code calls only stored proc `detail.PromoteDetail`.

Evidence:
- `Kor.Operations.App/StandardDetails/KorStandardsPromoterRepository.cs:26`

The proc validates target confidence, errors on unknown detail, rolls back, and is idempotent on already-matching confidence.

Evidence:
- `C:/VIsual Studio Projects/KOR.Drafter/db/066_PromoteDetailProcAndPromoter.sql:55`
- `C:/VIsual Studio Projects/KOR.Drafter/db/066_PromoteDetailProcAndPromoter.sql:68`
- `C:/VIsual Studio Projects/KOR.Drafter/db/066_PromoteDetailProcAndPromoter.sql:71`
- `C:/VIsual Studio Projects/KOR.Drafter/db/066_PromoteDetailProcAndPromoter.sql:117`

Optional connection degradation is sound: both KorStandards connections bind with nullable config access, and repos are only created when non-empty.

Evidence:
- `Kor.Operations.App/CompositionModules/CompositionHelpers.cs:53`
- `Kor.Operations.App/CompositionModules/CompositionHelpers.cs:54`
- `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs:52`
- `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs:54`

Filtered-index SET options are a deployment dependency, not a code finding. Microsoft documents that `QUOTED_IDENTIFIER` must be ON to create filtered indexes, and current scripts do not set it explicitly; SSMS/modern drivers normally satisfy this, but the safer script posture would set it.

Source:
- Microsoft Learn, `SET QUOTED_IDENTIFIER`: https://learn.microsoft.com/en-us/sql/t-sql/statements/set-quoted-identifier-transact-sql

## Verdict

SHIP WITH FIXES: handle `DecideAsync` failures cleanly, make outbox mark-done/mark-failed conditional on pending status, add retry-failed support or fix the claim, account for outbox rows in delete behavior, and harden the promoter permission assertion against role membership.
