SET XACT_ABORT ON;
GO

-- Phase A cull — hard-delete CanonicalOrg rows that have ZERO references
-- across every known FK table. These are dead-weight imports that never
-- got linked to a project / opportunity / bid / award / signal / person /
-- alias / news / engagement / pursuit / permit / brief / enrichment /
-- trigger. Safe to delete because no FKs point to them.
--
-- This is the cull mentioned in the BD platform vision — get rid of
-- canonicals KOR will never engage with so AI signal isn't diluted.
--
-- Audit before delete: 14,909 orphan candidates across all Kinds.

BEGIN TRAN;

DECLARE @orphans TABLE (Id BIGINT PRIMARY KEY);

INSERT INTO @orphans (Id)
SELECT co.Id
FROM opportunities.CanonicalOrg co
WHERE ISNULL(co.KorProjectsCount, 0) = 0
  AND co.LastKorProjectAtUtc IS NULL
  -- Include retired MPI rows — FK constraints still apply even on retired rows.
  AND NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory m
        WHERE m.ProponentCanonicalOrgId=co.Id OR m.ArchitectCanonicalOrgId=co.Id
             OR m.GeneralContractorCanonicalOrgId=co.Id OR m.StructuralEngineerCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.Opportunities o WHERE o.BuyerCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityBids b WHERE b.BidderCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityInterestedFirms i WHERE i.ResolvedCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityAwards a WHERE a.AwardingCanonicalOrgId=co.Id OR a.AwardedToCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagements c WHERE c.BuyerCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.KorPursuits k WHERE k.BuyerCanonicalOrgId=co.Id OR k.LostToCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.BuildingPermit bp WHERE bp.OwnerCanonicalOrgId=co.Id OR bp.ApplicantCanonicalOrgId=co.Id OR bp.ContractorCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.ArchitectDisplacementBriefs adb WHERE adb.ArchitectCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation pa WHERE pa.CanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelSignal s WHERE s.CanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelAction a WHERE a.CanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelRisk r WHERE r.CanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelNarrative n WHERE n.CanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelWork w WHERE w.CanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelProjectAction ipa WHERE ipa.TargetCanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.IntelProjectKeyPerson ipk WHERE ipk.CanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.NewsArticleOrgMention nm WHERE nm.CanonicalOrgId=co.Id)
  AND NOT EXISTS (SELECT 1 FROM opportunities.BdResearchTriggers t WHERE t.CanonicalOrgId=co.Id);

DECLARE @candidateCount int = (SELECT COUNT(*) FROM @orphans);
PRINT 'Orphan candidates identified: ' + CONVERT(varchar(20), @candidateCount);

-- Some orphan candidates may still have OrgAlias or CanonicalOrgEnrichment
-- rows (which we didn't filter out — those are owned BY the canonical not
-- referenced FROM somewhere meaningful). Clean those up first.

DECLARE @orphanEnrichmentIds TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @orphanEnrichmentIds (Id)
SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId IN (SELECT Id FROM @orphans);

-- Delete dependent Intel rows referencing the orphan enrichments
DELETE FROM opportunities.IntelSignal             WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelAction             WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelRisk               WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelNarrative          WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelWork               WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelPersonAffiliation  WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelPerson             WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelProject            WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelProjectAction      WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelProjectRisk        WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelProjectKeyPerson   WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);
DELETE FROM opportunities.IntelProjectSignal      WHERE SourceEnrichmentId IN (SELECT Id FROM @orphanEnrichmentIds);

DELETE FROM opportunities.CanonicalOrgEnrichment WHERE Id IN (SELECT Id FROM @orphanEnrichmentIds);
PRINT 'Orphan enrichments deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE FROM opportunities.OrgAlias WHERE CanonicalOrgId IN (SELECT Id FROM @orphans);
PRINT 'Orphan OrgAlias rows deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE FROM opportunities.CanonicalOrg WHERE Id IN (SELECT Id FROM @orphans);
PRINT 'Orphan CanonicalOrg rows deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

SELECT 'Final CanonicalOrg count' AS Stat, COUNT(*) AS Value FROM opportunities.CanonicalOrg;
SELECT 'By Kind post-cull' AS Stat, Kind, COUNT(*) AS Cnt FROM opportunities.CanonicalOrg GROUP BY Kind ORDER BY Cnt DESC;

PRINT 'Migration 83 orphan cull complete.';

COMMIT TRAN;
GO
