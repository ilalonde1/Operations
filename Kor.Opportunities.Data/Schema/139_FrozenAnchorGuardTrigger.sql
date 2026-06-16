USE [KorOpportunitiesDb];
GO

/* =====================================================================
   139 — Schema-level guard for the frozen self-anchors.
   ---------------------------------------------------------------------
   KorStructural / KorClient are KOR's own identity rows ("frozen kinds").
   Until now they were protected ONLY in application code (the importers'
   FROZEN_KINDS set), which already failed once: a dedup pass collapsed the
   KorStructural anchor (id 38918) to kind 'Firm', aborting the entire
   research-import sweep firm-wide (repaired by migration 138).

   This makes the invariant DB-enforced, not code-hoped: any UPDATE that
   would change a frozen-anchor row's Kind, or set its RetiredAtUtc, is
   rejected and the transaction rolled back. Promoting a non-frozen row up
   TO a frozen kind is still allowed (that is how the anchor is (re)created
   — migration 138 does exactly that), because the guard keys off the OLD
   (deleted) kind, not the new one.

   If an anchor must ever be re-pointed deliberately, disable this trigger
   for the duration of that maintenance, then re-enable it.

   Idempotent: CREATE OR ALTER.
   ===================================================================== */

CREATE OR ALTER TRIGGER opportunities.TR_CanonicalOrg_ProtectFrozenAnchor
ON opportunities.CanonicalOrg
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Cheap exit: only the Kind / RetiredAtUtc columns can violate the invariant.
    IF NOT (UPDATE(Kind) OR UPDATE(RetiredAtUtc))
        RETURN;

    IF EXISTS (
        SELECT 1
        FROM deleted d
        INNER JOIN inserted i ON i.Id = d.Id
        WHERE d.Kind IN (N'KorStructural', N'KorClient')
          AND (i.Kind <> d.Kind OR i.RetiredAtUtc IS NOT NULL)
    )
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(
            N'Frozen-anchor protection: a KorStructural/KorClient CanonicalOrg row cannot be kind-changed or retired. Disable opportunities.TR_CanonicalOrg_ProtectFrozenAnchor deliberately if this is an intentional anchor change.',
            16, 1);
        RETURN;
    END
END
GO
