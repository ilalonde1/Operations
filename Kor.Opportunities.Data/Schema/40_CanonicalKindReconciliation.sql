USE [KorOpportunitiesDb];
GO

/*
    Round 25 — Canonical Kind reconciliation.

    Background: research imports + the earlier python->SQL seed left CanonicalOrg.Kind
    fragmented and partly illegal (values not in OrgKinds): 'Client' (public buyers),
    'Developer' (no enum member until this round), 'Firm' (KOR's own row). And many
    research orgs were mis-kinded (24 BC developers stored as 'Buyer', 22 competitors
    as 'Vendor', etc.).

    Fix: re-kind every research-enriched org by its AUTHORITATIVE enrichment ProviderName
    (most-specific wins), map the illegal 'Client' -> 'Buyer', and normalize KOR's own
    'Firm' row to the new 'KorStructural' marker. KorClient and KorStructural are
    protected from research re-kinding (client/self status outranks a research label).

    OrgKinds is now: KorClient | Vendor | Buyer | Architect | GC | Subcontractor
                   | Developer | Competitor | KorStructural | Unknown
*/

PRINT '=== Kind distribution BEFORE ===';
SELECT Kind, COUNT(*) AS N FROM opportunities.CanonicalOrg GROUP BY Kind ORDER BY COUNT(*) DESC;

-- 1) Re-kind research-enriched orgs by best (most-specific) provider.
--    Protect KorClient (authoritative Deltek client) and KorStructural (self).
;WITH OrgBest AS (
    SELECT e.CanonicalOrgId,
           MIN(CASE e.ProviderName
                   WHEN 'ArchitectPipelineResearch' THEN 1
                   WHEN 'CompetitionResearch'       THEN 2
                   WHEN 'ContractorResearch'        THEN 3
                   WHEN 'DeveloperPipelineResearch' THEN 4
                   WHEN 'IndigenousDevResearch'     THEN 4
                   WHEN 'PublicSectorResearch'      THEN 5
                   ELSE 99
               END) AS Pri
    FROM opportunities.CanonicalOrgEnrichment e
    GROUP BY e.CanonicalOrgId
)
UPDATE co
SET    Kind = CASE ob.Pri
                 WHEN 1 THEN 'Architect'
                 WHEN 2 THEN 'Competitor'
                 WHEN 3 THEN 'GC'
                 WHEN 4 THEN 'Developer'
                 WHEN 5 THEN 'Buyer'
             END,
       UpdatedAtUtc = sysdatetimeoffset()
FROM   opportunities.CanonicalOrg co
JOIN   OrgBest ob ON ob.CanonicalOrgId = co.Id
WHERE  ob.Pri <= 5
  AND  co.Kind NOT IN ('KorClient', 'KorStructural')
  AND  co.Kind <> CASE ob.Pri
                      WHEN 1 THEN 'Architect'
                      WHEN 2 THEN 'Competitor'
                      WHEN 3 THEN 'GC'
                      WHEN 4 THEN 'Developer'
                      WHEN 5 THEN 'Buyer'
                  END;
PRINT 'Re-kinded research orgs by provider: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 2) Map any remaining illegal 'Client' -> 'Buyer' (partner-graph stragglers etc.)
UPDATE opportunities.CanonicalOrg
SET    Kind = 'Buyer', UpdatedAtUtc = sysdatetimeoffset()
WHERE  Kind = 'Client';
PRINT 'Client -> Buyer: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 3) Normalize KOR's own row (illegal 'Firm' -> 'KorStructural')
UPDATE opportunities.CanonicalOrg
SET    Kind = 'KorStructural', UpdatedAtUtc = sysdatetimeoffset()
WHERE  Kind = 'Firm';
PRINT 'Firm -> KorStructural: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 4) Guard: any remaining illegal kinds?
PRINT '=== Remaining ILLEGAL kinds (should be empty) ===';
SELECT Kind, COUNT(*) AS N
FROM   opportunities.CanonicalOrg
WHERE  Kind NOT IN ('KorClient','Vendor','Buyer','Architect','GC','Subcontractor',
                    'Developer','Competitor','KorStructural','Unknown')
GROUP  BY Kind;

PRINT '=== Kind distribution AFTER ===';
SELECT Kind, COUNT(*) AS N FROM opportunities.CanonicalOrg GROUP BY Kind ORDER BY COUNT(*) DESC;

PRINT 'Migration 40: canonical Kind reconciliation complete.';
GO
