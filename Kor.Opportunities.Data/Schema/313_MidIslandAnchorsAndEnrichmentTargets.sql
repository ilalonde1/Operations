-- Migration 313 (2026-09-04): anchor the mid-Island applicant firms and mark
-- them as roster-enrichment targets.
--
-- Domains confirmed by Apollo organization search on 2026-09-04, not guessed.
--
-- ⛔ NORTHLAND DEVELOPMENTS LTD., the newest Parksville applicant (353 Moilliet
--    St S, rezone RS-1 to RS-3 + OCP amendment, 14 Jan 2026), is deliberately
--    NOT created and NOT linked to anything. We hold SIX Northland companies —
--    Properties (CL00299, 369 KOR jobs), Asset Management (12 jobs), Mechanical,
--    School Division, Power, and NPC Builders — and it is none of them. Apollo
--    returns ZERO organizations for that name in British Columbia. Assuming a
--    link on a shared first word is the Starlight error, so it stays unlinked
--    until a person confirms who they are.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @anchors TABLE (Nm nvarchar(200), Dom nvarchar(200));
INSERT INTO @anchors (Nm, Dom) VALUES
 (N'WestUrban Developments Ltd',        N'westurban.ca'),
 (N'Seymour Pacific Developments',      N'seymourpacific.ca'),
 (N'Crowne Pacific Development Corp.',  N'crownepacific.com');

UPDATE co SET WebsiteDomain = a.Dom,
              Website = N'https://' + a.Dom + N'/',
              UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrg co
JOIN @anchors a ON a.Nm = co.DisplayName
WHERE co.RetiredAtUtc IS NULL AND (co.WebsiteDomain IS NULL OR co.WebsiteDomain = N'');

-- Mark every mid-Island applicant firm that HAS a domain as a roster target, so
-- tools/BdContactEnrich --roster-ingest --provider MidIslandApplicants picks
-- them up. The enricher selects on CanonicalOrgEnrichment.ProviderName.
DECLARE @firms TABLE (Nm nvarchar(200));
INSERT INTO @firms (Nm) VALUES
 (N'WestUrban Developments Ltd'), (N'Seymour Pacific Developments'),
 (N'Crowne Pacific Development Corp.'), (N'Radcliffe Development Corporation'),
 (N'D Akers Property Solutions'), (N'McElhanney'), (N'Zemcore Group Ltd'),
 (N'Ridge North America'), (N'Pennyfarthing Development Corporation'),
 (N'NSDA Architects'), (N'Ian Moxon Architect'), (N'Royop Development Corp'),
 (N'MacDonald Hagarty Architects (MHArchitects)'), (N'JM Architecture Inc.'),
 (N'Three Dog Ventures'), (N'Gibbins Road Holdings'), (N'Herold Engineering'),
 (N'Glotman Simpson Consulting Engineers');

INSERT INTO opportunities.CanonicalOrgEnrichment
    (CanonicalOrgId, ProviderName, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
SELECT co.Id, N'MidIslandApplicants', N'ok', 0, sysdatetimeoffset(), sysdatetimeoffset()
FROM opportunities.CanonicalOrg co
JOIN @firms f ON f.Nm = co.DisplayName
WHERE co.RetiredAtUtc IS NULL
  AND co.WebsiteDomain IS NOT NULL AND co.WebsiteDomain <> N''
  AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                  WHERE e.CanonicalOrgId = co.Id AND e.ProviderName = N'MidIslandApplicants');

SELECT 'roster targets' AS Section;
SELECT co.Id, co.Kind, LEFT(co.DisplayName, 40) AS Firm, co.WebsiteDomain,
       (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId = co.Id) AS PeopleBefore
FROM opportunities.CanonicalOrgEnrichment e
JOIN opportunities.CanonicalOrg co ON co.Id = e.CanonicalOrgId
WHERE e.ProviderName = N'MidIslandApplicants'
ORDER BY co.DisplayName;
GO
