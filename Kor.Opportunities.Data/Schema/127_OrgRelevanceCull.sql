-- 127_OrgRelevanceCull.sql
-- Second org cull (Ian, 2026-06-11), connectivity-based. m126 removed
-- junk NAMES; this removes orgs with no BD SURFACE: nothing live links
-- them, so there is nothing to research them for.
--
--   * zero-link: no active-MPI role, no people affiliations, no intel,
--     no award references at all -> import residue.
--   * award-only (GC / Developer / Buyer only): the org's only existence
--     is historical award records — the "not enriching past awards"
--     doctrine. Architect / Competitor award-only rows are KEPT (highest
--     BD-value kinds, only ~99 rows); KorClient is never culled.
--
-- Reversal is AUTOMATIC: Nightly-Refresh.ps1 clears m127 suppressions for
-- any org that has since gained a live link (new MPI role, affiliation,
-- or intel). m126 junk-name suppressions are NOT auto-revived — those
-- need a name fix first.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

DECLARE @now datetimeoffset = sysdatetimeoffset();

WITH Pool AS (
    SELECT co.Id, co.Kind
    FROM opportunities.CanonicalOrg co
    WHERE co.RetiredAtUtc IS NULL
      AND co.EnrichmentSuppressedAtUtc IS NULL
      AND co.Kind IN (N'Architect', N'GC', N'Developer', N'Buyer', N'Competitor')
      AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                      WHERE e.CanonicalOrgId = co.Id AND e.ProviderName = N'FirmNarrative' AND e.ResultJson IS NOT NULL)
),
Conn AS (
    SELECT p.Id, p.Kind,
        CASE WHEN EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory m WHERE m.RetiredAtUtc IS NULL
              AND (m.ArchitectCanonicalOrgId = p.Id OR m.GeneralContractorCanonicalOrgId = p.Id
                   OR m.StructuralEngineerCanonicalOrgId = p.Id OR m.ProponentCanonicalOrgId = p.Id)) THEN 1 ELSE 0 END AS HasMpi,
        CASE WHEN EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId = p.Id AND a.RetiredAtUtc IS NULL) THEN 1 ELSE 0 END AS HasPeople,
        CASE WHEN EXISTS (SELECT 1 FROM opportunities.IntelSignal s WHERE s.CanonicalOrgId = p.Id AND s.RetiredAtUtc IS NULL)
               OR EXISTS (SELECT 1 FROM opportunities.IntelAction x WHERE x.CanonicalOrgId = p.Id AND x.RetiredAtUtc IS NULL) THEN 1 ELSE 0 END AS HasIntel,
        CASE WHEN EXISTS (SELECT 1 FROM opportunities.OpportunityAwards aw
              WHERE aw.AwardingCanonicalOrgId = p.Id OR aw.AwardedToCanonicalOrgId = p.Id) THEN 1 ELSE 0 END AS HasAward
    FROM Pool p
)
UPDATE co SET
    EnrichmentSuppressedAtUtc = @now,
    EnrichmentSuppressedReason =
        CASE WHEN c.HasAward = 0
             THEN N'm127: zero-link — no project/people/intel/award surface; auto-revives if a live link appears'
             ELSE N'm127: award-only — historical award records are the only surface; auto-revives if a live link appears'
        END
FROM opportunities.CanonicalOrg co
JOIN Conn c ON c.Id = co.Id
WHERE c.HasMpi = 0 AND c.HasPeople = 0 AND c.HasIntel = 0
  AND (c.HasAward = 0 OR c.Kind IN (N'GC', N'Developer', N'Buyer'));
PRINT 'm127 suppressed: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm127 committed.';
GO

-- Verify: remaining researchable (un-enriched, un-suppressed) pool by kind.
SELECT co.Kind, COUNT(*) AS RemainingResearchable
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL AND co.EnrichmentSuppressedAtUtc IS NULL
  AND co.Kind IN (N'Architect', N'GC', N'Developer', N'Buyer', N'Competitor', N'KorClient')
  AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                  WHERE e.CanonicalOrgId = co.Id AND e.ProviderName = N'FirmNarrative' AND e.ResultJson IS NOT NULL)
GROUP BY co.Kind ORDER BY RemainingResearchable DESC;
GO
