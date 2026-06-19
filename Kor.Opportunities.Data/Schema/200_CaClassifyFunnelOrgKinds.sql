USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 200: classify CA funnel orgs left as Kind='Unknown' by ingest.
  Confident classifications only (firm name + permit/role context). Genuinely
  ambiguous SPE-style names (Delmas Pro, J3C Group), JV credit-line strings, and
  junk placeholders are intentionally left Unknown for the JV/junk triage pass.
  Engineers (Arup) map to Competitor since KOR's discipline is structural and
  OrgKinds has no Engineer value.
*/

BEGIN TRAN;

-- General contractors / builders
UPDATE opportunities.CanonicalOrg SET Kind = N'GC', UpdatedAtUtc = sysdatetimeoffset()
WHERE Kind = N'Unknown' AND Id IN
 (77000,77003,77001,  -- ARCO Murray / NorCal / National Holdings (design-build GC)
  77002,              -- Danco Builders Northwest
  77014,              -- Devcon Construction
  77007,              -- Green Valley Corp
  77011,              -- HA Builder Group
  76993,              -- Hanover RS Construction
  77010,              -- Holland Construction Inc
  76994,              -- Iron Construction
  76999,              -- Jemcor Construction Partners
  77009,              -- JTM Construction Group
  76997,              -- Level 10 Construction
  77025,              -- Nibbi Bros. Associates
  77017,              -- Oarcon Inc
  77019,              -- Pacific West Builders
  76992,              -- Premier Design & Build Group
  76995,              -- Roem Builders
  76996,              -- Truebeck Construction
  77016);             -- Uprite Construction

-- Developers
UPDATE opportunities.CanonicalOrg SET Kind = N'Developer', UpdatedAtUtc = sysdatetimeoffset()
WHERE Kind = N'Unknown' AND Id IN
 (77022,   -- 525 Capitol LP
  76948,   -- IMT Residential
  76947,   -- JL Real Estate Development (Jia Long USA)
  76816,   -- Manchester Financial Group
  76872,   -- SKK Developments
  77012,   -- Swenson
  77020,   -- TMHCO LLC
  77013,   -- UC Madera Owner LLC
  76815,   -- The Greenwald Company
  76930);  -- Tenderloin Neighborhood Development Corp

-- Architects
UPDATE opportunities.CanonicalOrg SET Kind = N'Architect', UpdatedAtUtc = sysdatetimeoffset()
WHERE Kind = N'Unknown' AND Id IN (77008, 74059);  -- Architects FORA, Perkins Eastman

-- Competitor (structural/MEP engineer)
UPDATE opportunities.CanonicalOrg SET Kind = N'Competitor', UpdatedAtUtc = sysdatetimeoffset()
WHERE Kind = N'Unknown' AND Id = 77015;  -- Arup

-- Buyers (public agencies, school/college districts, universities, hospitals)
UPDATE opportunities.CanonicalOrg SET Kind = N'Buyer', UpdatedAtUtc = sysdatetimeoffset()
WHERE Kind = N'Unknown' AND Id IN
 (76858,   -- California Dept. of General Services (DGS)
  76875,   -- City of Sacramento
  77030,   -- City of Vista
  77027,   -- City of West Covina
  76868,   -- CSU Sacramento
  76861,   -- Los Rios Community College District
  77029,   -- Manhattan Beach Unified School District
  76938,   -- MarinHealth Medical Center
  77028,   -- Modoc County
  76870,   -- Sacramento Municipal Utility District
  76939,   -- San Francisco University High School
  76935,   -- San Jose State University
  77031,   -- San Mateo Union School District
  76921,   -- UC Berkeley
  76864,   -- UC Davis Health
  76925,   -- UC Hastings College of the Law
  74069,   -- UC San Diego
  76923,   -- UCSF
  76934,   -- UC / Lawrence Berkeley National Laboratory
  76926,   -- City & County of San Francisco
  76924);  -- City & County of San Francisco (SFPUC)

DECLARE @still int = (SELECT COUNT(*) FROM opportunities.CanonicalOrg WHERE Kind = N'Unknown' AND Id IN (
  SELECT ProponentCanonicalOrgId FROM opportunities.MajorProjectsInventory WHERE Province='CA' AND ProponentCanonicalOrgId IS NOT NULL
  UNION SELECT ArchitectCanonicalOrgId FROM opportunities.MajorProjectsInventory WHERE Province='CA' AND ArchitectCanonicalOrgId IS NOT NULL));
PRINT CONCAT('Migration 200: CA funnel orgs still Unknown after classification: ', @still);

COMMIT TRAN;
GO
