SET XACT_ABORT ON;
GO

-- Add soft-retire columns to CanonicalOrg + retire the obvious procurement
-- noise rows. Sets the infrastructure so downstream consumers (briefs,
-- enrichment cron, dashboard, resolver) can skip retired orgs without
-- losing the bid history that lives in OpportunityBids /
-- OpportunityInterestedFirms.

IF COL_LENGTH('opportunities.CanonicalOrg', 'RetiredAtUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.CanonicalOrg
        ADD RetiredAtUtc DATETIMEOFFSET NULL,
            RetiredReason NVARCHAR(200) NULL;
END;
GO

-- Index for fast "active orgs only" filtering — most consumer queries
-- will filter on RetiredAtUtc IS NULL.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CanonicalOrg_RetiredAtUtc' AND object_id = OBJECT_ID('opportunities.CanonicalOrg'))
BEGIN
    CREATE INDEX IX_CanonicalOrg_RetiredAtUtc
        ON opportunities.CanonicalOrg(RetiredAtUtc)
        INCLUDE (Kind);
END;
GO

BEGIN TRAN;

DECLARE @retired int;

-- Phase B-1 — retire Vendor/Subcontractor/Unknown rows with no strong BD signal.
-- "Strong BD signal" = MPI role / Award / CRM / Pursuit / BuildingPermit /
-- Architect-displacement-brief link. Weak signals (bids, interested-firms,
-- person-affiliations, news mentions) are not enough to keep on their own
-- for these three Kinds.

UPDATE co
SET co.RetiredAtUtc = sysdatetimeoffset(),
    co.RetiredReason = N'R95-extra Phase B: procurement noise (Vendor/Sub/Unknown kind with no strong BD signal)'
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
  AND co.Kind IN (N'Vendor', N'Subcontractor', N'Unknown')
  AND ISNULL(co.KorProjectsCount, 0) = 0
  AND co.LastKorProjectAtUtc IS NULL
  AND NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory m
        WHERE m.ProponentCanonicalOrgId=co.Id OR m.ArchitectCanonicalOrgId=co.Id
             OR m.GeneralContractorCanonicalOrgId=co.Id OR m.StructuralEngineerCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.Opportunities o WHERE o.BuyerCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityAwards a WHERE a.AwardingCanonicalOrgId=co.Id OR a.AwardedToCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagements c WHERE c.BuyerCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.KorPursuits k WHERE k.BuyerCanonicalOrgId=co.Id OR k.LostToCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.BuildingPermit bp WHERE bp.OwnerCanonicalOrgId=co.Id OR bp.ApplicantCanonicalOrgId=co.Id OR bp.ContractorCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.ArchitectDisplacementBriefs adb WHERE adb.ArchitectCanonicalOrgId=co.Id);
SET @retired = @@ROWCOUNT;
PRINT 'Phase B-1 retired (Vendor/Sub/Unknown procurement noise): ' + CONVERT(varchar(20), @retired);

-- Post-state distribution
SELECT 'Active by Kind' AS Stat, Kind, COUNT(*) AS Cnt
FROM opportunities.CanonicalOrg
WHERE RetiredAtUtc IS NULL
GROUP BY Kind
ORDER BY Cnt DESC;

SELECT 'Retired total' AS Stat, COUNT(*) AS Cnt FROM opportunities.CanonicalOrg WHERE RetiredAtUtc IS NOT NULL;

PRINT 'Migration 84 RetiredAtUtc schema + Phase B-1 retire complete.';

COMMIT TRAN;
GO
