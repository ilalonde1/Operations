/*
    Kor.OpportunitiesDb migration 266.

    Canonical-org merge ledger. When BdCanonicalDedup merges duplicate firms it
    hard-deletes the loser CanonicalOrg row; this ledger records the authoritative
    "MergedFrom -> MergedInto" consolidation so any later reference to a retired
    identity (e.g. a drain envelope generated against the loser id before the
    merge) resolves to the surviving canonical org. This is the org-side
    equivalent of the people merge audit + natural-key resolution.
*/
IF OBJECT_ID(N'opportunities.CanonicalOrgMerge','U') IS NULL
BEGIN
    CREATE TABLE opportunities.CanonicalOrgMerge (
        MergedFromCanonicalOrgId  BIGINT           NOT NULL PRIMARY KEY,
        MergedIntoCanonicalOrgId  BIGINT           NOT NULL,
        MergedAtUtc               DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        Reason                    NVARCHAR(200)    NULL
    );

    -- NO FOREIGN KEYS - this is mandatory. An FK to CanonicalOrg would (a) trip
    -- VerifySchemaAsync's FK-completeness guard and brick the dedup tool, and
    -- (b) break DeleteLosersAsync. Integrity is maintained by chain-collapse in
    -- the merge transaction, not by referential constraints.
    CREATE INDEX IX_CanonicalOrgMerge_MergedInto
        ON opportunities.CanonicalOrgMerge (MergedIntoCanonicalOrgId);
END;
GO

PRINT 'Migration 266 complete.';
GO
