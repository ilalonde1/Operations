USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 253: BD audit 2026-06-19 M2.
  Re-anchor exactly two live affiliations whose SourceEnrichmentId points to a
  retired org's enrichment. The affiliations already belong to the live orgs:
    - 13739 -> CEI Architecture live org 4552
    - 19435 -> Arcadis IBI live org 153
*/
BEGIN TRAN;

DECLARE @Provider nvarchar(100) = N'BdAudit20260619-M2Reanchor';

DECLARE @Targets TABLE
(
    AffiliationId bigint NOT NULL PRIMARY KEY,
    LiveOrgId bigint NOT NULL,
    Label nvarchar(100) NOT NULL,
    AnchorEnrichmentId bigint NULL
);

INSERT INTO @Targets (AffiliationId, LiveOrgId, Label)
VALUES
    (13739, 4552, N'CEI Architecture'),
    (19435, 153,  N'Arcadis IBI');

MERGE opportunities.CanonicalOrgEnrichment WITH (HOLDLOCK) AS target
USING
(
    SELECT LiveOrgId
    FROM @Targets
    GROUP BY LiveOrgId
) AS source
ON target.CanonicalOrgId = source.LiveOrgId
AND target.ProviderName = @Provider
WHEN NOT MATCHED THEN
    INSERT (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
    VALUES (source.LiveOrgId, @Provider, N'Manual', 0, sysdatetimeoffset(), sysdatetimeoffset());

UPDATE t
SET AnchorEnrichmentId =
    COALESCE(
        (
            SELECT MIN(e.Id)
            FROM opportunities.CanonicalOrgEnrichment e
            WHERE e.CanonicalOrgId = t.LiveOrgId
              AND e.ProviderName <> @Provider
        ),
        (
            SELECT MIN(e.Id)
            FROM opportunities.CanonicalOrgEnrichment e
            WHERE e.CanonicalOrgId = t.LiveOrgId
              AND e.ProviderName = @Provider
        )
    )
FROM @Targets t;

UPDATE a
SET SourceEnrichmentId = t.AnchorEnrichmentId,
    SourceProviderName = e.ProviderName,
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPersonAffiliation a
JOIN @Targets t ON t.AffiliationId = a.Id
JOIN opportunities.CanonicalOrgEnrichment e ON e.Id = t.AnchorEnrichmentId
WHERE a.SourceEnrichmentId <> t.AnchorEnrichmentId;
PRINT 'IntelPersonAffiliation SourceEnrichmentId rows re-anchored: ' + CONVERT(varchar(20), @@ROWCOUNT);

IF EXISTS
(
    SELECT 1
    FROM @Targets t
    JOIN opportunities.IntelPersonAffiliation a ON a.Id = t.AffiliationId
    JOIN opportunities.CanonicalOrgEnrichment e ON e.Id = a.SourceEnrichmentId
    WHERE a.CanonicalOrgId <> t.LiveOrgId
       OR e.CanonicalOrgId <> t.LiveOrgId
)
BEGIN
    THROW 51253, 'M2 re-anchor verification failed: affiliation or enrichment is not on the expected live org.', 1;
END;

PRINT 'Migration 253 complete: M2 affiliation enrichment anchors corrected.';
COMMIT TRAN;
GO
