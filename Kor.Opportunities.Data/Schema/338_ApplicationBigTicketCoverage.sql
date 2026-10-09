-- Migration 338: generalise the EXISTING application scope authority, retain its name
-- and columns for dossiers/ad-hoc consumers. No parallel view or C# classifier.
-- COVERS: all application-kind sources, including mixed transports with bd.kind
-- metadata, and undismissed opportunities; disabled sources keep their existing rows.
-- The small-work conditions from 316/331 are unchanged. The only precedence exception
-- is Calgary land-use redesignations: all are retained as buildable-form change signals,
-- not asserted to exceed a dollar threshold. A title's "redesignation" must not turn
-- this feed into sign permits via the existing broad LIKE '%SIGN%' condition.
-- DOES NOT COVER: monetary scale, planning approval, recency or truth/completeness of
-- source text. Empty scope passes unless the title triggers an exclusion. Generic
-- residential titles can exclude real multi-unit projects; keep the App reveal visible.
-- SAME-CLASS MISS: "replace the single-family house with 80 apartments" still matches
-- the inherited single-family exclusion. Conversely, a minor interior refit described
-- without any exclusion words remains big-ticket. Neither is repaired by broader coverage.
-- This historical view name no longer implies Island-only coverage: regional reports
-- must explicitly select their SourceName/Municipality. IsDated remains independent.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
ALTER VIEW opportunities.vIslandApplications
AS
SELECT
    o.Id                              AS OpportunityId,
    s.Name                            AS SourceName,
    REPLACE(REPLACE(REPLACE(s.Name, '_DevelopmentApplications', ''),
            '_WhatsBuilding', ''), '_', ' ')  AS Municipality,
    o.Name                            AS Title,
    o.BuyerContactName                AS Applicant,
    o.ProjectAddress                  AS Address,
    o.EstimatedValue                  AS EstimatedValue,
    o.RfpReleaseDate                  AS FiledDate,
    CAST(CASE WHEN o.RfpReleaseDate IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IsDated,
    d.Descr                           AS Scope,
    CAST(CASE WHEN s.Name = N'Calgary_LandUseRedesignation' THEN 1
    WHEN o.Name LIKE '%RESIDENTIAL BUILDING PERMIT%' OR o.Name LIKE '%RESIDENTIAL PERMIT%'
      OR o.Name LIKE '%SFD%'            OR d.Descr LIKE '%SFD%'
      OR d.Descr LIKE '%single family%' OR d.Descr LIKE '%single-family%'
      OR d.Descr LIKE '%single dwelling%'
      OR d.Descr LIKE '%tenant improvement%' OR o.Name LIKE '%TENANT IMPROVEMENT%'
      OR d.Descr LIKE '%First TI%'
      OR o.Name LIKE '%ALTERATION%'     OR d.Descr LIKE '%alteration%'
      OR d.Descr LIKE '%secondary suite%' OR o.Name LIKE '%SECONDARY SUITE%'
      OR d.Descr LIKE '%carriage house%'  OR d.Descr LIKE '%coach house%'
      OR d.Descr LIKE '%garden suite%'    OR d.Descr LIKE '%accessory dwelling%'
      OR o.Name LIKE '%SIGN%'           OR d.Descr LIKE '%ign Permit%'
      OR d.Descr LIKE '%deck%'   OR d.Descr LIKE '%fence%'  OR d.Descr LIKE '%pool%'
      OR d.Descr LIKE '%shed%'   OR d.Descr LIKE '%garage%' OR d.Descr LIKE '%carport%'
      OR d.Descr LIKE '%retaining wall%' OR d.Descr LIKE '%driveway%'
      OR d.Descr LIKE '%demolition%'
      OR d.Descr LIKE '%service connection%' OR o.Name LIKE '%SERVICE CONNECTION%'
      OR d.Descr LIKE '%ENGINEERING PERMIT%'
    THEN 0 ELSE 1 END AS bit)         AS IsBigTicket
FROM opportunities.Opportunities o
JOIN (
    SELECT DISTINCT ob.OpportunityId, ob.OpportunitySourceId
    FROM opportunities.OpportunityObservations ob
) x ON x.OpportunityId = o.Id
JOIN opportunities.OpportunitySources s ON s.Id = x.OpportunitySourceId
LEFT JOIN opportunities.OpportunitySourceMappings m ON m.OpportunitySourceId=s.Id AND m.[Key]='bd.kind'
OUTER APPLY (
    SELECT TOP 1 ob2.Description AS Descr
    FROM opportunities.OpportunityObservations ob2
    WHERE ob2.OpportunityId = o.Id
    ORDER BY ob2.Id DESC
) d
WHERE o.DismissedAtUtc IS NULL   -- 331: a dismissed or merged-away row is not an application
  AND (s.SourceType IN (20,21,22)
       OR (s.SourceType IN (1,2,3,5,99)
           AND LOWER(REPLACE(LTRIM(RTRIM(m.ValueJson)), '"', '')) = 'application'));
GO
