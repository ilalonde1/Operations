USE [KorOpportunitiesDb];
GO

/* =====================================================================
   152 — Re-home contacts mis-affiliated to the wrong firm.
   ---------------------------------------------------------------------
   People whose work email domain matches a DIFFERENT canonical org than the
   firm they were filed under (scrape/pattern artifacts that attached an owner's
   rep or a partner-firm's staff to a project's architect, e.g. Concord Pacific
   staff filed under W.T. Leung; Grosvenor + City of Vancouver staff under Hariri
   Pontarini; Urban One Builders staff under Acton Ostry; City of Calgary staff
   under RJC). The email domain is the truth source of the employer.

   Fix per affiliation:
     - if the person already has a live affiliation to the true org -> RETIRE
       the wrong one;
     - else -> REPOINT the existing affiliation's CanonicalOrgId to the true org
       (preserves all NOT-NULL provenance columns + NaturalKey).

   Guards (no-guessing): true-org targets that are composites ('/') or
   concatenated-name junk are excluded; personal/government/shared domains are
   excluded; same-family matches (current + true share first word) are excluded;
   and — critically — rows where the person's SURNAME appears in their CURRENT
   firm name are excluded, since those are principals-of-the-current-firm with a
   stray foreign email (Paul Fast @ Fast+Epp; Tom Clark @ Clark/Kjos), NOT real
   moves. Idempotent.
   ===================================================================== */

IF OBJECT_ID('tempdb..#plan') IS NOT NULL DROP TABLE #plan;
WITH orgdom AS (
  SELECT co.Id, co.DisplayName,
    CASE WHEN CHARINDEX('/', s) > 0 THEN LEFT(s, CHARINDEX('/', s)-1) ELSE s END AS Dom
  FROM opportunities.CanonicalOrg co
  CROSS APPLY (SELECT s = REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(co.Website))),'https://',''),'http://',''),'www.','')) z
  WHERE co.RetiredAtUtc IS NULL AND NULLIF(LTRIM(RTRIM(co.Website)),'') IS NOT NULL
    AND co.DisplayName NOT LIKE '%/%' AND co.DisplayName NOT LIKE '%Authority%Authority%'
),
aff AS (
  SELECT a.Id AS AffId, p.Id AS PersonId, p.DisplayName AS Person,
    CASE WHEN CHARINDEX(' ', p.DisplayName) > 0
         THEN RIGHT(p.DisplayName, CHARINDEX(' ', REVERSE(p.DisplayName)) - 1) ELSE p.DisplayName END AS LastName,
    LOWER(SUBSTRING(p.Email, CHARINDEX('@',p.Email)+1, 200)) AS EmailDom,
    co.Id AS CurOrgId, co.DisplayName AS CurOrg,
    CASE WHEN CHARINDEX('/', cs) > 0 THEN LEFT(cs, CHARINDEX('/', cs)-1) ELSE cs END AS CurOrgDom
  FROM opportunities.IntelPersonAffiliation a
  JOIN opportunities.IntelPerson p ON p.Id=a.IntelPersonId AND p.RetiredAtUtc IS NULL
  JOIN opportunities.CanonicalOrg co ON co.Id=a.CanonicalOrgId AND co.RetiredAtUtc IS NULL
  CROSS APPLY (SELECT cs = REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(co.Website))),'https://',''),'http://',''),'www.','')) z
  WHERE a.RetiredAtUtc IS NULL AND NULLIF(LTRIM(RTRIM(p.Email)),'') IS NOT NULL
    AND NULLIF(LTRIM(RTRIM(co.Website)),'') IS NOT NULL
),
matched AS (
  SELECT aff.AffId, aff.PersonId, aff.CurOrg, od.Id AS TrueOrgId, od.DisplayName AS TrueOrg,
    ROW_NUMBER() OVER (PARTITION BY aff.AffId ORDER BY od.Id) AS rn
  FROM aff
  JOIN orgdom od ON od.Dom = aff.EmailDom AND od.Id <> aff.CurOrgId
  WHERE aff.EmailDom <> aff.CurOrgDom
    AND aff.EmailDom NOT IN ('gmail.com','hotmail.com','yahoo.com','outlook.com','telus.com','shaw.ca',
        'gov.bc.ca','gov.ab.ca','alberta.ca','icloud.com','bosaproperties.com','fraserhealth.ca','interiorhealth.ca')
    AND LEFT(aff.CurOrg, CHARINDEX(' ', aff.CurOrg + ' ')) <> LEFT(od.DisplayName, CHARINDEX(' ', od.DisplayName + ' '))
    AND aff.CurOrg NOT LIKE '%' + aff.LastName + '%'   -- exclude principals-of-current-firm (stray email)
)
SELECT m.AffId, m.PersonId, m.TrueOrgId,
  CAST(CASE WHEN EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation x
       WHERE x.IntelPersonId=m.PersonId AND x.CanonicalOrgId=m.TrueOrgId AND x.RetiredAtUtc IS NULL)
       THEN 1 ELSE 0 END AS BIT) AS AlreadyHasTrue
INTO #plan
FROM matched m
WHERE m.rn = 1 AND m.AffId <> 18815;   -- 18815: Christopher Usih (CBE/Richmond ambiguous)

/* Retire the wrong affiliation where the person already has the true-org link. */
UPDATE a SET a.RetiredAtUtc = SYSDATETIMEOFFSET(),
       a.RetiredReason = N'Re-homed: email domain matches true employer (migration 152)',
       a.UpdatedAtUtc = SYSDATETIMEOFFSET()
FROM opportunities.IntelPersonAffiliation a
JOIN #plan p ON p.AffId = a.Id
WHERE p.AlreadyHasTrue = 1 AND a.RetiredAtUtc IS NULL;

/* Repoint the affiliation to the true org where the person lacks that link. */
UPDATE a SET a.CanonicalOrgId = p.TrueOrgId,
       a.Notes = LEFT(ISNULL(a.Notes + N' | ', N'') + N'Re-homed from wrong firm by email domain (migration 152)', 4000),
       a.UpdatedAtUtc = SYSDATETIMEOFFSET()
FROM opportunities.IntelPersonAffiliation a
JOIN #plan p ON p.AffId = a.Id
WHERE p.AlreadyHasTrue = 0 AND a.RetiredAtUtc IS NULL;

DROP TABLE #plan;

/* Bonus: fix the contaminated website that caused spurious Fraser-Health matches.
   "Betty Dion Enterprises Limited" (an accessibility consultancy) had its Website
   set to fraserhealth.ca — clearly wrong. Null it rather than guess the real URL. */
UPDATE opportunities.CanonicalOrg
SET Website = NULL, UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 38990 AND Website LIKE '%fraserhealth.ca%';
GO
