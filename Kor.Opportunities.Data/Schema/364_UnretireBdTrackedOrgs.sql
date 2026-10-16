-- Migration 364 (2026-10-16): un-retire seven companies the BD team tracks.
--
-- The 22 June cleanup retired twelve org rows as "permit/BC-registry-derived non-AEC... 0 KOR linkage". Its linkage test
-- did not look at CrmEngagements, and all twelve carry a pursuit from the team's tracking spreadsheet (imported 30 May).
-- Seven of the twelve are the only record of that company, so they come back. The other five (PCI Development 70839,
-- Fastmark 70837, Qualico/Streetside 70834, Thurber 70844, Traine 70861) were stubs beside a proper record; they stay
-- retired and tools/BdTrackingImport/companies.csv points the spreadsheet at the proper record instead.
-- Guarded: touches only these ids, only while retired, and says how many it changed.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = NULL,
    RetiredReason = NULL,
    Notes = CONCAT(ISNULL(Notes + N' | ', N''), N'2026-10-16: un-retired (migration 364); tracked by the BD team, retired 22 Jun by a cleanup that ignored pursuits.'),
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id IN (70838, 70852, 76660, 70868, 70870, 70835, 70842)
  AND RetiredAtUtc IS NOT NULL;

SELECT @@ROWCOUNT AS UnRetired;

SELECT Id, DisplayName, CASE WHEN RetiredAtUtc IS NULL THEN 'live' ELSE 'retired' END AS State
FROM opportunities.CanonicalOrg WHERE Id IN (70838, 70852, 76660, 70868, 70870, 70835, 70842) ORDER BY Id;
GO
