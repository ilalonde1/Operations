TASK D - CLOSE THE LOOP: APPROVE -> ENQUEUE -> PROMOTE -> PLACEABLE

GOAL
When a gatekeeper APPROVES a linked detail's document, the linked KorStandards detail must become
Confidence='human-confirmed' (which makes it placeable). Task C built the DB contract
(detail.PromoteDetail proc + standards_promoter EXECUTE-only login + StandardDetailPromotionOutbox
table - all LIVE). Task D wires the app: enqueue an outbox row in the SAME transaction as the
approval, drain it by calling the proc through a promoter connection, and show a Promotion Queue.

Do NOT build, run tests, execute SQL, or touch git. Claude verifies (including a LIVE end-to-end
run) after you finish.

WHAT EXISTS (read and match)
- The approval transaction is StandardDetailsRepository.DecideAsync (Repository.cs:508). It opens
  one transaction `tx` and does: UPDATE DocumentVersions status -> INSERT ApprovalRecords -> INSERT
  AuditEvents -> COMMIT. decision==1 is approve, decision==2 is reject. The outbox INSERT goes
  inside this SAME transaction, just before COMMIT, and ONLY for approve.
- Task C objects, now live:
  * KorStandards: proc detail.PromoteDetail(@DetailNumber, @ToConfidence, @Basis, @ChangedBy) -
    @ToConfidence must be 'human-confirmed' or 'rejected'; idempotent; unknown DetailNumber raises.
    Login standards_promoter has EXECUTE on that proc and nothing else. Password
    'KorStandardsPromote!2026'.
  * KorTransmittals: dbo.StandardDetailPromotionOutbox
    (PromotionOutboxId, DocumentId, DocumentVersionId, DetailNumber, TargetConfidence,
     RequestedByUserId, RequestedByUserName, RequestedUtc, Status tinyint {0 pending,1 done,2 failed},
     AttemptCount, ProcessedUtc, ResultMessage, ErrorMessage). Index on (Status, RequestedUtc).
- Connection-string chain (from Task A, follow it): App.config <connectionStrings>;
  AppConfigKeys.ConnectionStrings; DatabaseOptions (AppOptions.cs:13); CompositionHelpers
  .GetDatabaseOptions() (line 51, use the OPTIONAL nullable pattern - not GetRequiredConnectionString).
- Repo style: StandardDetailsRepository.cs (SqlClient, records, AddParam/AddNVarChar helpers,
  SqlTimeouts.UiFacing). The window creates repos in StandardDetailsWindow_Loaded (xaml.cs:46);
  Task A already added `_korStandardsRepo`. Dialog pattern: CreateStandardDocumentWindow /
  LinkDetailWindow.

DO THIS

1. New connection KorStandardsPromoterDb (the standards_promoter login):
   App.config:  Server=KOR-APP01\SQLEXPRESS;Database=KorStandards;User Id=standards_promoter;
                Password=KorStandardsPromote!2026;Encrypt=True;TrustServerCertificate=True;
   Add the key to AppConfigKeys.ConnectionStrings, the property to DatabaseOptions, and the
   OPTIONAL binding in GetDatabaseOptions (absent -> "" -> promotion simply unavailable, no crash).

2. DecideAsync (StandardDetailsRepository.cs): add a `string actorUserName` parameter (the real
   Environment.UserName - the actor GUID is only a hash, so the outbox/history need the real name).
   Inside the existing transaction, AFTER the audit insert and BEFORE COMMIT, when decision == 1:
     - SELECT DetailNumber FROM dbo.Documents WHERE DocumentId=@docId (in-tx).
     - If DetailNumber IS NOT NULL, INSERT one row into dbo.StandardDetailPromotionOutbox
       (DocumentId, DocumentVersionId, DetailNumber, TargetConfidence='human-confirmed',
        RequestedByUserId=actorUserId, RequestedByUserName=actorUserName, Status=0) on `tx`.
     - If DetailNumber IS NULL, enqueue nothing (an unlinked approval promotes nothing - that is
       correct, not an error).
   Reject (decision==2) enqueues NOTHING (a rejected document version does not reject the estate
   detail). Do not change reject behaviour.
   Update the one caller (DecideSelectedVersionAsync in Logic.cs) to pass Environment.UserName.

3. New file KorStandardsPromoterRepository.cs (namespace Kor.Operations.StandardDetails,
   internal sealed, SqlClient) - the WRITE side, used only by the processor:
     - ctor(string promoterConnectionString).
     - `Task<(bool ok, string message)> PromoteAsync(string detailNumber, string toConfidence,
        string basis, string changedBy)` that opens the promoter connection and
        EXEC detail.PromoteDetail with those params; reads the proc's result row into `message`;
        returns ok=true. On SqlException returns ok=false with the error message. No other SQL.

4. Outbox access on StandardDetailsRepository (KorTransmittals side):
     - record StandardDetailsOutboxRow(long PromotionOutboxId, long DocumentId, long DocumentVersionId,
       string DetailNumber, string TargetConfidence, string? RequestedByUserName, DateTime RequestedUtc,
       byte Status, int AttemptCount, DateTime? ProcessedUtc, string? ResultMessage, string? ErrorMessage).
     - `LoadOutboxAsync()` - newest first, all rows (for the queue view).
     - `LoadPendingOutboxAsync()` - Status=0 rows, oldest first (for the processor).
     - `MarkOutboxDoneAsync(long id, string resultMessage)` - Status=1, ProcessedUtc=now,
       ResultMessage set, AttemptCount+1.
     - `MarkOutboxFailedAsync(long id, string errorMessage)` - Status=2, ProcessedUtc=now,
       ErrorMessage set, AttemptCount+1.

5. A processor method (put it on the window or a tiny helper, whichever matches the codebase best):
   `ProcessPendingPromotionsAsync()` - if the promoter repo is null (no connection), do nothing and
   return a friendly "promotion not configured" note. Else: load pending outbox rows; for EACH,
   in its own try/catch, call KorStandardsPromoterRepository.PromoteAsync(row.DetailNumber,
   row.TargetConfidence, basis: $"Approved in Operations Standard Details by {row.RequestedByUserName}",
   changedBy: row.RequestedByUserName ?? "operations"); on ok -> MarkOutboxDoneAsync; on failure ->
   MarkOutboxFailedAsync. One row's failure must not stop the others. Return a short summary
   (processed N, failed M).

6. Wire it:
   - In StandardDetailsWindow_Loaded, create `_promoterRepo` (KorStandardsPromoterRepository) only
     when DatabaseOptions.KorStandardsPromoterDb is non-empty; else null.
   - After a SUCCESSFUL approve in DecideSelectedVersionAsync, call ProcessPendingPromotionsAsync()
     best-effort (try/catch; a promotion failure must NOT undo the approval - it is already
     committed - just surface a warning banner and leave the row pending/failed for the queue).
   - Add a "Promotion Queue..." button (ActionButton style) that opens a new PromotionQueueWindow.

7. PromotionQueueWindow.xaml/.xaml.cs (styled like LinkDetailWindow): a read-only DataGrid bound to
   StandardDetailsOutboxRow (DetailNumber, TargetConfidence, Status as text Pending/Done/Failed,
   Requested By, RequestedUtc, Result/Error message); a "Process Pending" button that calls
   ProcessPendingPromotionsAsync then refreshes; a Refresh button; a Close button. Takes the
   StandardDetailsRepository (for LoadOutbox) and the processor delegate/callback.

CONSTRAINTS
- Only the StandardDetails module + the four connection-string files change. Do not alter the proc,
  the outbox schema, KorStandardsReadRepository, LinkDetailWindow, or the workflow's reject/publish.
- The outbox INSERT MUST be inside DecideAsync's existing transaction (atomic with the approval).
- The promotion write happens ONLY through detail.PromoteDetail via the promoter connection - never
  a direct KorStandards table write from the app.
- If either KorStandards connection is missing, the module still opens and approve still works;
  promotion just does not run.
- Match existing helpers and naming; do not introduce a second SqlClient style or a DI framework change.

END with an adversarial self-review: (a) prove the outbox row is atomic with the approval (same tx,
rolled back together on failure); (b) prove a promotion failure cannot roll back or undo a committed
approval; (c) prove an unlinked (no DetailNumber) approval enqueues nothing and does not error;
(d) prove re-processing is safe (idempotent proc + Status transitions); (e) prove nothing writes a
KorStandards table directly; (f) file+line of every edit.
