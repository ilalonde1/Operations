/* Kor.OpportunitiesDb migration 271: persist website domains for canonical-org
   identity resolution. Application code maintains the extracted value using
   CanonicalOrgResolver.ExtractWebsiteDomain. */

-- Filtered index below requires these SET options ON (else CREATE INDEX fails, msg 1934).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH(N'opportunities.CanonicalOrg', N'WebsiteDomain') IS NULL
BEGIN
    ALTER TABLE opportunities.CanonicalOrg
        ADD WebsiteDomain nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CanonicalOrg_WebsiteDomain'
      AND object_id = OBJECT_ID(N'opportunities.CanonicalOrg'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_CanonicalOrg_WebsiteDomain
        ON opportunities.CanonicalOrg (WebsiteDomain)
        WHERE WebsiteDomain IS NOT NULL AND RetiredAtUtc IS NULL;
END;
GO