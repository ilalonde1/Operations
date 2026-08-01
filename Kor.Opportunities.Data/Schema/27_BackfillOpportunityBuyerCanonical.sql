/*
    Kor.OpportunitiesDb migration 27.
    Backfills opportunities.Opportunities.BuyerCanonicalOrgId for rows inserted
    while SqlOpportunityStore had a resolver path but DI did not pass the resolver.
*/

-- First use aliases that were already classified for the active-opportunity buyer source.
UPDATE o
SET    BuyerCanonicalOrgId = co.Id
FROM   opportunities.Opportunities o
JOIN   opportunities.OrgAlias a
       ON a.RawName = o.BuyerName
      AND a.Source = 'Opportunities.Buyer'
JOIN   opportunities.CanonicalOrg co
       ON co.Id = a.CanonicalOrgId
WHERE  o.BuyerCanonicalOrgId IS NULL;
GO

-- Then fall back to the same normalized-name logic used by CanonicalOrg.NormalizedName.
UPDATE o
SET    BuyerCanonicalOrgId = (
    SELECT TOP 1 co.Id
    FROM   opportunities.CanonicalOrg co
    WHERE  co.NormalizedName =
        LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            o.BuyerName, ' ', ''), '.', ''), ',', ''), CHAR(39), ''), '-', ''), '&', ''), '/', ''), '(', ''), ')', ''), '+', ''))
    ORDER BY CASE WHEN co.ClendorClientId IS NOT NULL THEN 0 ELSE 1 END, co.Id)
FROM   opportunities.Opportunities o
WHERE  o.BuyerCanonicalOrgId IS NULL
  AND  o.BuyerName IS NOT NULL
  AND  o.BuyerName <> '';
GO

PRINT 'Migration 27 complete.';
GO
