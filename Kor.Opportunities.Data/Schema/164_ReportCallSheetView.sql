USE [KorOpportunitiesDb];
GO

/* =====================================================================
   164 — vReportCallSheet: the single source of truth for the contact
   (call-sheet) sections of BD reports.
   ---------------------------------------------------------------------
   Reports currently hand-embed named contacts/emails, which drifts from
   the graph. This view emits the best-ranked, confidence-marked contacts
   per canonical org straight from IntelPerson/Affiliation, so:
     - the markdown reports can be sourced from one query (no hand-typing),
     - the future in-app report builder renders from the same view.
   Companion to vOrgWarmPath (which gives per-org COUNTS; this gives ROWS).

   ConfidenceMark mirrors the report legend:
     ✓ = verified / authoritative on-file (Hunter or asis)
     ~ = probable (PatternInferred)
     (blank) = named-only (reach via office)
   Consumers filter by CanonicalOrgId (often joined to MPI by region) and
   take RankInOrg <= N for the top decision-makers.
   ===================================================================== */
GO

CREATE OR ALTER VIEW opportunities.vReportCallSheet AS
WITH c AS (
  SELECT
    co.Id AS CanonicalOrgId, co.DisplayName AS OrgName, co.Kind AS OrgKind,
    p.Id AS PersonId, p.DisplayName AS PersonName, a.Title, a.Department,
    p.Email, p.EmailSource, p.EmailConfidence,
    CASE WHEN NULLIF(LTRIM(RTRIM(p.Email)),'') IS NULL THEN 0 ELSE 1 END AS IsEmailable,
    CASE
      WHEN NULLIF(LTRIM(RTRIM(p.Email)),'') IS NULL THEN N'named-only'
      WHEN p.EmailSource IN ('Hunter','asis') THEN N'verified'
      ELSE N'probable'
    END AS EmailTier,
    CASE
      WHEN a.Title LIKE '%Founder%' OR a.Title LIKE '%CEO%' OR a.Title LIKE '%President%'
        OR a.Title LIKE '%Chief%' OR a.Title LIKE '%Managing Principal%' OR a.Title LIKE '%Partner%' THEN 0
      WHEN a.Title LIKE '%Principal%' OR a.Title LIKE '%Vice President%' OR a.Title LIKE '% VP%'
        OR a.Title LIKE '%Director%' THEN 1
      WHEN a.Title LIKE '%Manager%' OR a.Title LIKE '%Associate%' OR a.Title LIKE '%Lead%' THEN 2
      ELSE 3
    END AS SeniorityRank
  FROM opportunities.CanonicalOrg co
  JOIN opportunities.IntelPersonAffiliation a ON a.CanonicalOrgId = co.Id AND a.RetiredAtUtc IS NULL
  JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId AND p.RetiredAtUtc IS NULL
  WHERE co.RetiredAtUtc IS NULL
)
SELECT
  c.CanonicalOrgId, c.OrgName, c.OrgKind, c.PersonId, c.PersonName, c.Title, c.Department,
  c.Email, c.EmailSource, c.EmailConfidence, c.IsEmailable, c.EmailTier,
  CASE c.EmailTier WHEN N'verified' THEN N'✓' WHEN N'probable' THEN N'~' ELSE N'' END AS ConfidenceMark,
  ROW_NUMBER() OVER (PARTITION BY c.CanonicalOrgId ORDER BY
     c.IsEmailable DESC,
     CASE c.EmailSource WHEN 'Hunter' THEN 0 WHEN 'asis' THEN 1 WHEN 'PatternInferred' THEN 2 ELSE 3 END,
     c.SeniorityRank,
     ISNULL(c.EmailConfidence,0) DESC,
     c.PersonName) AS RankInOrg
FROM c;
GO
