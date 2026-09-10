-- Migration 317 (2026-09-10): Perkins&Will intel banked from the brief prepared
-- for Omar Alcazar. Canonical org 69688 (perkinswill.com, Deltek CL00499).
--
-- ⛔ 74059 Perkins Eastman is a DIFFERENT FIRM and is deliberately untouched.
--
-- Sources: KOR project records and ClendorProjectAssoc for the shared-project
-- history; Perkins&Will's own newsroom, Dec 2025 - Sep 2026, for everything else.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

IF OBJECT_ID('tempdb..#f') IS NOT NULL DROP TABLE #f;
CREATE TABLE #f (FactType nvarchar(60), Body nvarchar(max), SourceUrl nvarchar(400));

INSERT INTO #f (FactType, Body, SourceUrl) VALUES
(N'WarmChannel',
 N'⭐ KOR HAS BEEN ON NINE PROJECTS WITH PERKINS&WILL AS ARCHITECT, all BC, 2018-2019, found via ClendorProjectAssoc.Role = ''Architect'': Harry Jerome Phase 1 M4 / Phase 2 T2+M2 / Phase 3 T1+M1 (Darwin Properties, North Vancouver) · 900 Granville St and 600 Robson St (Bonnis Properties) · River District Parcel 16.2 (Wesgroup) · 5696 Alberta St (Nicola Wealth) · 357-475 West 41st Ave (Coromandel). ⚠ ALL were billed to the DEVELOPER, not to P&W. The one record under P&W''s own Deltek account CL00499 - 90068-01 ICBC Head Office North Vancouver - was never invoiced. So say "we have been on nine projects with you", never "you have been our client". Harry Jerome is the one to name.',
 NULL),
(N'CompetitorNote',
 N'⛔ MASS TIMBER SEAT LOST TO ASPECT. Feb 2026 P&W published a prefabricated modular mass-timber housing study for Canada, co-invested $250,000 by DIGITAL''s Housing Growth Innovation programme. Named partners: ASPECT STRUCTURAL ENGINEERS, Introba, and SFU''s School of Interactive Arts & Technology. A funded Canadian mass-timber housing R&D programme with a competitor in the structural chair, in KOR''s lane. Quoted on it: Derek Newby (Canadian Regional Director) and Kathy Wardle (Regional Director of Regenerative Design) - both already in our contact set. They are also designing BCIT''s zero-carbon tall timber student housing, announced Jan 2026.',
 N'https://perkinswill.com/news/new-study-from-perkinswill-offers-mass-timber-solution-to-canadas-housing-crisis/'),
(N'MarketFocus',
 N'FIRM STRATEGY 2026, from their own newsroom. (1) BUILDING A GLOBAL RETAIL PRACTICE FROM SCRATCH - Jan 2026 hired five principals at once, all from the same Amsterdam design and engineering firm: Matt Billerbeck (Seattle, Retail Practice Leader), Sophie Bramall (Dallas), Erich Dohrer (Chicago), Sarah Holstedt (Seattle), James Mellor (San Diego). CEO Phil Harrison: "build the most sought-after retail practice in the world". Portfolio is shopping-centre repositioning, stadium neighbourhoods, airports, TOD, vertically integrated commercial hubs. (2) SCIENCE & TECHNOLOGY PIVOT TO APPLIED SCIENCE - Dec 2025 appointed Jeff Zynda firmwide S&T Practice Leader for robotics and automation, energy systems and storage, AI, quantum computing and machine learning, across all 32 studios. (3) BOUGHT INTO NEW YORK - merged with A+I (Architecture Plus Information) effective Sep 2025.',
 N'https://perkinswill.com/news/'),
(N'MarketFocus',
 N'LEADERSHIP BUILD-OUT Dec 2025 - Sep 2026: ⭐DEREK NEWBY appointed to the FIRMWIDE BOARD OF DIRECTORS (Feb 2026, with Brad Zizmor) AND elevated to the RAIC COLLEGE OF FELLOWS for design innovation and climate leadership (May 2026); he runs Vancouver AND Calgary and is Canadian Regional Director - the single most useful contact for work outside BC. Also: Yanel de Angel to Chief Marketing Officer and Vickie Alani to MD Boston (Feb); Rosa Maria Colina MD Miami (Jan); Todd Buchanan MD Seattle (Dec 2025); a new MD for Toronto and Ottawa (Nov 2025); firmwide Director of R&D hired Apr 2026 from running an MIT accelerator; Chris Hardie to firmwide Design Directors (Mar); Bay Area landscape architecture leadership (Sep). CEO Phil Harrison, Atlanta, in post since 2006.',
 N'https://perkinswill.com/news/'),
(N'MarketFocus',
 N'LIVE WORK OUTSIDE BC, 2026. CANADA: University of Calgary Interdisciplinary Science and Innovation Centre (their Calgary studio; KOR''s own awards data shows P&W took the architectural services contract, 2019 - treat current status as a question); Toronto Metropolitan University Daphne Cockwell Health Sciences Complex, a 28-storey health education tower they position as their model for dense urban campus design; Calgary studio relocated to the historic Glenbow building, 822 11th Ave SW, Beltline. US: University of Louisville Health Sciences Center with Champlin | EOP; University of Houston RAD Center (2026 AIA Design Excellence, Education Facilities); LSU Health Shreveport Emerging Viral Threats Lab (2026 Lab Design Excellence); UVA Health emergency department expansion (IIDA); a mall-to-college-campus conversion honoured by ULI Americas, Jul 2026. INTERNATIONAL: science and technology museum in Suzhou, China (Mar 2026); triple winner 2026 RIBA International Awards; London studio named Studio of the Year at Clerkenwell Design Week; two AIA National Awards (Jun 2026).',
 N'https://perkinswill.com/news/'),
(N'RiskNote',
 N'⛔ DO NOT CONFLATE WITH PERKINS EASTMAN (canonical 74059, perkinseastman.com, 21 people held). Different firm. Also distinct: canonical 76335 "Scott Edwards Architecture (with Perkins & Will)" is a joint-venture row, not the firm. ⚠ P&W publishes NO headcount - neither their website nor their releases give one; "32 studios" is safe (their own Dec 2025 release), a staff number is not. Ownership was NOT verified as of 2026-09-10 - do not assert it.',
 NULL);

INSERT INTO opportunities.OrgFact
    (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
SELECT CONVERT(varchar(40), HASHBYTES('SHA1',
           '69688|' + f.FactType + '|' + LEFT(f.Body, 80) + '|PW-2026-09-10'), 2),
       69688, f.FactType, f.Body, f.SourceUrl,
       N'Perkins&Will brief for Omar Alcazar, 2026-09-10',
       CAST('2026-09-10' AS datetimeoffset), N'High', SYSUTCDATETIME(), N'BrainDecompose-2026-09-10'
FROM #f f
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgFact e
                  WHERE e.CanonicalOrgId = 69688
                    AND e.NaturalKey = CONVERT(varchar(40), HASHBYTES('SHA1',
                        '69688|' + f.FactType + '|' + LEFT(f.Body, 80) + '|PW-2026-09-10'), 2)
                    AND e.RetiredAtUtc IS NULL);

SELECT 'facts written on Perkins&Will' AS Section;
SELECT f.FactType, LEFT(f.Body, 66) AS Body
FROM opportunities.OrgFact f
WHERE f.CanonicalOrgId = 69688 AND f.CreatedBy = N'BrainDecompose-2026-09-10'
ORDER BY f.FactType;
GO
