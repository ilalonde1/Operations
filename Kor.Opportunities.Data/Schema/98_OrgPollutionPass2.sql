SET XACT_ABORT ON;
GO

-- Org pollution pass 2. The orgs/batch-006 drain (2026-06-07) exposed
-- additional pollution patterns that m96 missed:
--   * "% Award" / "% Proposal" bid-status (variants of m96 patterns)
--   * "Architecture: ..." prefix (parser artifact)
--   * "... and ... Architects" / "... + ..." / "... | ..." / "... in
--     association with ..." multi-entity rows (real JVs but cannot
--     be researched as a single org — the individual JV partners
--     already exist as separate canonicals)
--   * Placeholder roles ("architectural designer", "Architectural
--     Technologist", etc.) — not entity names, just role labels
--   * Person-concatenated names ("Core Architects Inc. - Eva Kochanski")

BEGIN TRAN;

DECLARE @retired int;

-- Bid-status pollution that escaped m96 (Award / Proposal suffix variants)
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra m98: bid-status pollution (Award/Proposal/Submission suffix)'
WHERE RetiredAtUtc IS NULL
  AND (
       DisplayName LIKE N'% Award'
    OR DisplayName LIKE N'% Award %'
    OR DisplayName LIKE N'% Proposal'
    OR DisplayName LIKE N'% Submission'
    OR DisplayName LIKE N'% Submissions'
    OR DisplayName LIKE N'% Bidder'
    OR DisplayName LIKE N'% Bidder %'
    OR DisplayName LIKE N'% Award:%'
    OR DisplayName LIKE N'% Tender'
    OR DisplayName LIKE N'% Tender %');
SET @retired = @@ROWCOUNT;
PRINT 'Bid-status pollution pass 2 retired: ' + CONVERT(varchar(20), @retired);

-- Multi-entity research-artifact rows (JV-shorthand, not real entities)
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra m98: multi-entity research artifact (individual JV partners exist as separate canonicals)'
WHERE RetiredAtUtc IS NULL
  AND (
       DisplayName LIKE N'% and %Architect%' AND DisplayName LIKE N'%Architect% and %'
    OR DisplayName LIKE N'% + %Architecture%' OR DisplayName LIKE N'%Architecture% + %'
    OR DisplayName LIKE N'% + %Architect%' OR DisplayName LIKE N'%Architects% + %'
    OR DisplayName LIKE N'% | %' AND DisplayName LIKE N'%Architect%'
    OR DisplayName LIKE N'% in association with %'
    OR DisplayName LIKE N'% In Association With %'
    OR DisplayName LIKE N'%Architect% with %Architect%'
    OR DisplayName LIKE N'%Architecture and %Architecture%');
SET @retired = @@ROWCOUNT;
PRINT 'Multi-entity research-artifact rows retired: ' + CONVERT(varchar(20), @retired);

-- Placeholder role-not-entity names
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra m98: role placeholder, not an entity'
WHERE RetiredAtUtc IS NULL
  AND LOWER(DisplayName) IN (
      N'architectural designer', N'architectural design', N'architectural and design services',
      N'architectural technologist', N'architectural technologists', N'architect aibc',
      N'architecture', N'architectural firm', N'architecture firm',
      N'architecture office', N'architecture studio', N'design office');
SET @retired = @@ROWCOUNT;
PRINT 'Placeholder role rows retired: ' + CONVERT(varchar(20), @retired);

-- Architecture: prefix parser artifact
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra m98: Architecture: prefix parser artifact'
WHERE RetiredAtUtc IS NULL
  AND DisplayName LIKE N'Architecture: %';
SET @retired = @@ROWCOUNT;
PRINT 'Architecture: prefix retired: ' + CONVERT(varchar(20), @retired);

-- Person-concatenated names (DisplayName contains " - <FirstName LastName>" pattern)
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'R95-extra m98: org name with concatenated person name'
WHERE RetiredAtUtc IS NULL
  AND DisplayName LIKE N'% - [A-Z]% [A-Z]%'
  AND DisplayName NOT LIKE N'% - [0-9]%'  -- exclude addresses like "Building - 123 Main St"
  AND DisplayName NOT LIKE N'% - The %'
  AND (DisplayName LIKE N'%Architect%' OR DisplayName LIKE N'%Construction%' OR DisplayName LIKE N'%Engineer%'
       OR DisplayName LIKE N'%Group%' OR DisplayName LIKE N'%Inc%' OR DisplayName LIKE N'%Ltd%');
SET @retired = @@ROWCOUNT;
PRINT 'Person-concat names retired: ' + CONVERT(varchar(20), @retired);

SELECT 'Total retired CanonicalOrg now' AS Stat, COUNT(*) AS Cnt
FROM opportunities.CanonicalOrg WHERE RetiredAtUtc IS NOT NULL;

PRINT 'Migration 98 complete.';

COMMIT TRAN;
GO
