TASK C - THE KorStandards WRITE CONTRACT AND THE PROMOTION OUTBOX (SQL files only)

GOAL
When a gatekeeper APPROVES a detail's document in the app (later task), the linked KorStandards
detail must be promoted to Confidence='human-confirmed' with an audit row - but the desktop app
must NEVER get broad write access to KorStandards, and an approval must still commit even if
KorStandards is momentarily unreachable. Task C builds the two DB halves of that contract:
  1. In KorStandards: a stored proc that does the promotion, plus a narrow EXECUTE-only login.
  2. In KorTransmittals: an OUTBOX table the app writes in the same transaction as the approval,
     which a processor (next task) drains by calling the proc.

Write SQL FILES only. Do NOT build, run, execute SQL, or touch git. Claude verifies; Ian runs
each as sa in SSMS (001-style: each file forces its own USE + a wrong-database guard).

VERIFIED SCHEMA (already live - do not re-create these; the proc writes them)
detail.Detail (KorStandards):
   Id UNIQUEIDENTIFIER PK, DetailNumber NVARCHAR(24) NOT NULL (UNIQUE index UX_Detail_Number),
   Title, Discipline, Confidence NVARCHAR(32) NOT NULL,
   CHECK CK_Detail_Confidence: Confidence IN ('unverified','content-verified','human-confirmed','rejected').
detail.DetailHistory (KorStandards):
   Id UNIQUEIDENTIFIER PK, DetailId UNIQUEIDENTIFIER NOT NULL FK->detail.Detail(Id),
   FromConfidence NVARCHAR(32) NULL, ToConfidence NVARCHAR(32) NOT NULL,
   Basis NVARCHAR(1000) NOT NULL, ChangedBy NVARCHAR(150) NOT NULL,
   ChangedAtUtc DATETIMEOFFSET default sysdatetimeoffset().

FILE 1  -  C:\VIsual Studio Projects\KOR.Drafter\db\066_PromoteDetailProcAndPromoter.sql
   Header comment (what/when/before-after) + `USE KorStandards;` + wrong-database guard +
   `SELECT DB_NAME() AS [You are here];` (copy the guard shape from
   Operations\db\KorTransmittals\001_BaselineAsFound.sql).

   a) CREATE OR ALTER PROCEDURE detail.PromoteDetail
        @DetailNumber   NVARCHAR(24),
        @ToConfidence   NVARCHAR(32),      -- expected 'human-confirmed' (also allow 'rejected')
        @Basis          NVARCHAR(1000),
        @ChangedBy      NVARCHAR(150)
      Behaviour, SET XACT_ABORT ON, one transaction:
        - Validate @ToConfidence IN ('human-confirmed','rejected'); else RAISERROR (16) - this proc
          only performs gatekeeper outcomes, not arbitrary confidence writes.
        - Resolve @id = Id, @from = Confidence FROM detail.Detail WHERE DetailNumber=@DetailNumber.
          If none, RAISERROR('PromoteDetail: unknown DetailNumber %s', 16, 1, @DetailNumber) so the
          caller records a failure. (RAISERROR is the signal the outbox processor stores.)
        - IDEMPOTENT: if @from = @ToConfidence, COMMIT and RETURN 0 without a second history row
          (re-draining the same outbox row must be harmless).
        - Else UPDATE detail.Detail SET Confidence=@ToConfidence WHERE Id=@id; INSERT
          detail.DetailHistory (DetailId, FromConfidence, ToConfidence, Basis, ChangedBy)
          VALUES (@id, @from, @ToConfidence, @Basis, @ChangedBy).
        - Return the affected DetailId via a single-row SELECT (so the caller can log it).

   b) The narrow login. Guard each step with existence checks (re-runnable):
        IF NOT EXISTS in sys.server_principals: CREATE LOGIN standards_promoter WITH PASSWORD =
          'KorStandardsPromote!2026', CHECK_POLICY = OFF;
        IF NOT EXISTS in sys.database_principals: CREATE USER standards_promoter FOR LOGIN
          standards_promoter;
        GRANT EXECUTE ON detail.PromoteDetail TO standards_promoter;
      ⛔ Grant NOTHING else. No SELECT/INSERT/UPDATE on any table. Ownership chaining lets this
      dbo-owned proc write detail.Detail/DetailHistory while the caller can touch ONLY the proc.
      End with an assertion block: the proc exists, the grant exists, and standards_promoter has
      no other object permissions (SELECT from sys.database_permissions for that grantee and
      RAISERROR if more than the single EXECUTE is present). Print a confirmation row.

FILE 2  -  C:\VIsual Studio Projects\Operations\db\KorTransmittals\005_PromotionOutbox.sql
   Header + `USE KorTransmittals;` + the same wrong-database guard + You-are-here.
   CREATE TABLE dbo.StandardDetailPromotionOutbox (guard with IF OBJECT_ID IS NULL):
        PromotionOutboxId    bigint IDENTITY(1,1) PRIMARY KEY,
        DocumentId           bigint NOT NULL,
        DocumentVersionId    bigint NOT NULL,
        DetailNumber         nvarchar(24) NOT NULL,
        TargetConfidence     nvarchar(32) NOT NULL,
        RequestedByUserId    uniqueidentifier NOT NULL,
        RequestedByUserName  nvarchar(150) NULL,   -- real Windows username; actor GUID is a hash
        RequestedUtc         datetime2 NOT NULL CONSTRAINT DF_PromoOutbox_Req DEFAULT (SYSUTCDATETIME()),
        Status               tinyint  NOT NULL CONSTRAINT DF_PromoOutbox_Status DEFAULT (0),  -- 0 pending, 1 done, 2 failed
        AttemptCount         int      NOT NULL CONSTRAINT DF_PromoOutbox_Attempt DEFAULT (0),
        ProcessedUtc         datetime2 NULL,
        ResultMessage        nvarchar(1000) NULL,
        ErrorMessage         nvarchar(2000) NULL
      Plain nvarchar message columns - NOT json blobs. FK DocumentId->dbo.Documents(DocumentId).
      Index IX_PromoOutbox_Status ON (Status, RequestedUtc) so the processor finds pending rows.
      End with a count/assert + print.

CONSTRAINTS
- Two files, nothing else. No app/C# changes (that is Task D). No connection strings here.
- Do not alter detail.Detail, DetailHistory, or any existing table's shape.
- ASCII only; each file re-runnable (guards) and self-contained with its USE + guard.
- The promoter login is EXECUTE-only by design - do not add convenience grants.

END with an adversarial self-review: (a) prove standards_promoter cannot read or write any table
directly, only EXECUTE the proc (name the ownership-chaining reason); (b) prove re-draining the
same outbox row twice cannot double-promote or error (idempotency path); (c) prove an unknown
DetailNumber surfaces as a catchable error, not a silent success; (d) confirm neither file deletes
or reshapes anything live; (e) list each object created.
