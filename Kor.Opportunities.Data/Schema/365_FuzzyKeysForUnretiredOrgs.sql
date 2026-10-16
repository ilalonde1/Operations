-- Migration 365 (2026-10-16): the fuzzy keys of two orgs un-retired by migration 364.
--
-- Both were written before the 2026-09-05 normaliser change that folds "&" and a spaced "+" to "and", and retired rows
-- were not swept then. BdIntegrityCheck's org_fuzzy_key_stale names them with the expected key; this writes that value.
-- 71528 (MCW) is a deliberate override and is not touched. Guarded on the stale value so a re-run changes nothing.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

UPDATE opportunities.CanonicalOrg SET FuzzyNormalizedName = N'aandhsteel', UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 70838 AND FuzzyNormalizedName = N'ahsteel';
UPDATE opportunities.CanonicalOrg SET FuzzyNormalizedName = N'handwmechanical', UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 70868 AND FuzzyNormalizedName = N'hwmechanical';

SELECT Id, DisplayName, FuzzyNormalizedName FROM opportunities.CanonicalOrg WHERE Id IN (70838, 70868);
GO
