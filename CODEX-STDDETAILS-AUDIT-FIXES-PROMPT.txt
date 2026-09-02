TASK E-FIX - APPLY THE SIX CONFIRMED AUDIT FINDINGS

These are the verified findings from docs/standard-details-adversarial-audit-2026-09-02.md. Each is
real and traced. Fix exactly these; do not refactor beyond them. Do NOT build, run, execute SQL, or
touch git - Claude verifies after. Keep the existing SqlClient/record/helper style; additive and
minimal.

FIX 1 (audit #1) - DecideAsync failure must not escape as an unhandled exception.
File: Kor.Operations.App/StandardDetails/StandardDetailsWindow.Logic.cs, DecideSelectedVersionAsync
(~line 483). The call `await _repo.DecideAsync(...)` is not wrapped, unlike PublishSelectedVersionAsync
which catches SqlException. Wrap the DecideAsync call (and the RowsAffected handling) in try/catch:
on SqlException/Exception, show a friendly banner + MessageBox ("Approval failed. No changes were
committed.") and RETURN without running promotion. Match the pattern already used in
PublishSelectedVersionAsync. The promotion-processing block stays in its OWN existing try/catch after
a successful decide.

FIX 2 (audit #2 and #8) - a document with outbox/variant rows cannot be deleted.
File: StandardDetailsRepository.cs, DeleteRecordAsync (~line 368-418). The delete batch removes
ApprovalRecords, PublicationRecords, DocumentVersions, Documents, orphan FileBlobs - but NOT
dbo.StandardDetailPromotionOutbox rows (FK from 005) nor dbo.DocumentVariants rows (FK from 002), so
the Documents delete fails on those FKs. Add, inside the same transaction and in FK-safe order:
  - DELETE FROM dbo.StandardDetailPromotionOutbox WHERE DocumentId=@docId;  (before Documents)
  - after DELETE DocumentVersions and before DELETE Documents:
    DELETE FROM dbo.DocumentVariants WHERE DocumentId=@docId;
Final order: outbox -> approvals -> publications -> DocumentVersions -> DocumentVariants -> Documents
-> orphan FileBlobs. Guard each new DELETE so it is harmless when the table/rows do not exist is NOT
needed (both tables exist), but keep it inside the existing transaction so a failure rolls back whole.

FIX 3 (audit #3) - mark-done/mark-failed can overwrite terminal state.
File: StandardDetailsRepository.cs, MarkOutboxDoneAsync (~637) and MarkOutboxFailedAsync (~651). Both
UPDATE ... WHERE PromotionOutboxId=@id only. Add `AND Status = 0` to both WHERE clauses so a row that
another processor already moved to a terminal state is not overwritten. (A 0-row update is fine - it
just means someone else finished it.)

FIX 4 (audit #4) - failed rows are not retryable.
Add retry so a failed promotion can be re-attempted:
  - StandardDetailsRepository.cs: add `internal async Task<int> ResetFailedOutboxToPendingAsync()`
    that UPDATE dbo.StandardDetailPromotionOutbox SET Status=0, ErrorMessage=NULL WHERE Status=2;
    returns rows affected.
  - PromotionQueueWindow.xaml/.xaml.cs: add a "Retry Failed" button next to "Process Pending" that
    calls ResetFailedOutboxToPendingAsync(), then runs the same processor as Process Pending, then
    refreshes. If zero failed rows, show an info note.

FIX 5 (audit #5) - divergent details go human-confirmed but are NOT placeable; make that visible.
Placeability requires VariantsDiverge=0 (see detail.vw_PaletteCatalog), but the read repo and picker
never expose VariantsDiverge, so a user can link a divergent detail expecting it to become placeable.
  - KorStandardsReadRepository.cs: add VariantsDiverge to PaletteDetailRow and to LoadPaletteDetailsAsync
    (MAX(CAST(VariantsDiverge AS int)) grouped by DetailNumber; the view already exposes it).
  - LinkDetailWindow.xaml: add a "Diverges" column (Yes/No) to the grid.
  - LinkDetailWindow.xaml.cs, LinkSelected_Click: if the chosen row has VariantsDiverge=true, show an
    informational MessageBox first ("This detail has divergent size variants. Approving it will set it
    human-confirmed but it will NOT become placeable until the divergence is resolved. Link anyway?")
    with OK/Cancel; only proceed on OK. Linking is still allowed - this is informational, not a block.

FIX 6 (audit #6) - the promoter permission assertion ignores role membership.
File: C:\VIsual Studio Projects\KOR.Drafter\db\066_PromoteDetailProcAndPromoter.sql. The assertion
checks sys.database_permissions (direct object grants) but not sys.database_role_members. Add a check:
if standards_promoter is a member of ANY database role other than public
(SELECT from sys.database_role_members joined to sys.database_principals), print the offending roles
and RAISERROR('... standards_promoter is a member of a database role other than public ...', 16, 1).
Keep the script re-runnable (CREATE OR ALTER proc; guarded login/user). This proves EXECUTE-and-
nothing-else more completely.

CONSTRAINTS
- Only the files named above. Do not change the proc's logic, the outbox schema, the workflow's
  submit/reject/publish, or KorStandardsReadRepository's connection handling.
- FIX 2's new deletes MUST be inside DeleteRecordAsync's existing transaction.
- Keep everything additive and minimal; match existing helpers and naming.

END with an adversarial self-review: (a) prove FIX 2's delete order satisfies every FK (outbox->Documents,
variant->Documents, version->variant); (b) prove FIX 3 makes double-processing safe; (c) prove FIX 1
cannot swallow a real approval that DID commit; (d) prove FIX 5 still allows linking a divergent detail;
(e) file+line of every edit.
