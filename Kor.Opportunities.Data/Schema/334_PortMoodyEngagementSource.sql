-- Migration 334 (2026-10-08): Port Moody development applications, via EngagementHQ
-- (SourceType 22).
--
-- WHY THIS IS OVERDUE RATHER THAN NEW. `docs/KOR-EarlySignal-CoverageGap-2026-09-10.md`
-- verified this feed on 10 September and listed it as recommendation #2 of its own order
-- of work: "Port Moody — the adapter already exists; this is a config row." Then nothing
-- ran it. A month later the only Port Moody rows in the system were
-- `BidsTenders_PortMoody` and `BidsTendersAwards_PortMoody` — TENDERS, which by definition
-- arrive after the structural engineer was chosen. The municipality looked covered and was
-- not. §K of the weekly audit (migration 333) now counts coverage so that illusion is
-- visible, and tender sources are deliberately excluded from it.
--
-- VERIFIED LIVE 2026-10-08, not taken from the document:
--   66 projects, 35 application-shaped, 20 NOT archived.
--   Live examples: "2901-2909 St. George Street rezoning and development application",
--   "38 Shoreline Circle - OCP amendment and rezoning", "Coronation Park (3) - OCP
--   Amendment and Rezoning", "112 Moray Street - Rezoning and Development Permit".
--   The September measurement said 17 live; it has grown to 20.
--
-- ⚠ THE HOSTNAME IN THAT DOCUMENT REDIRECTS. It recorded `engageportmoody.ca`. Both
--    `engageportmoody.ca` and `www.engageportmoody.ca` 301 to **engage.portmoody.ca**,
--    which is what is configured here. A redirect works until it does not, and a feed that
--    depends on one fails in a way that looks like the publisher went quiet.
--
-- ⚠ includeArchived = false, following RDN (migration 311) for the same reason: 15 of the
--    35 are archived, meaning decided. The 20 live ones are the early signal.
--
-- ENABLED ON INSERT, deliberately. The seed-disabled rule in the Worker module guidance
-- exists because "a source whose SourceType the running binary has no provider for just
-- fails" — SourceType 22 has had a deployed provider since migration 311, and both RDN and
-- Vancouver are producing through it. Nothing waits on a deploy here.
--
-- WHAT THIS DOES NOT DO: Port Moody is 33,535 people. It does not touch the four
-- municipalities that actually matter by population — Burnaby 249,125, Richmond 209,937,
-- Langley Township 132,603, Delta 108,455 — none of which has a feed. See §K.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @pm uniqueidentifier;
SET @pm = NULL;
SELECT @pm = Id FROM opportunities.OpportunitySources
WHERE Name = N'PortMoody_DevelopmentApplications';

IF @pm IS NULL
BEGIN
    SET @pm = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds,
         CreatedAtUtc, UpdatedAtUtc, IsHistorical, QuartzManaged)
    VALUES (@pm, N'PortMoody_DevelopmentApplications', 22,
            N'https://engage.portmoody.ca',
            1, 86400, 120, sysdatetimeoffset(), sysdatetimeoffset(), 0, 0);
END
ELSE
BEGIN
    UPDATE opportunities.OpportunitySources
    SET SourceType = 22, BaseUrl = N'https://engage.portmoody.ca',
        IsEnabled = 1, UpdatedAtUtc = sysdatetimeoffset()
    WHERE Id = @pm;
END;

-- Following Vancouver's five keys exactly (migration 320). engagementhq.titleRegex,
-- applicantRegex and fileNumberRegex exist but neither live source sets them, so they keep
-- their code defaults here too: an untested regex in config is a silent filter.
MERGE opportunities.OpportunitySourceMappings AS t
USING (VALUES
    (N'engagementhq.projectsUrl',      N'https://engage.portmoody.ca/projects.json'),
    (N'engagementhq.buyerOverride',    N'City of Port Moody'),
    (N'engagementhq.cityOverride',     N'Port Moody'),
    (N'engagementhq.provinceOverride', N'BC'),
    (N'engagementhq.includeArchived',  N'false')
) AS s([Key], ValueJson)
ON t.OpportunitySourceId = @pm AND t.[Key] = s.[Key]
WHEN MATCHED AND ISNULL(t.ValueJson, N'') <> s.ValueJson
    THEN UPDATE SET ValueJson = s.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@pm, s.[Key], s.ValueJson, sysdatetimeoffset());

SELECT 'source' AS Section, Name, SourceType, IsEnabled, BaseUrl
FROM opportunities.OpportunitySources WHERE Id = @pm;

SELECT 'config' AS Section, [Key], ValueJson
FROM opportunities.OpportunitySourceMappings
WHERE OpportunitySourceId = @pm ORDER BY [Key];

-- Prove §K now credits Port Moody, which is the point of wiring it.
SELECT 'section K sees it' AS Section, m.Name, m.SourceNamePrefix, h.HeldBy
FROM opportunities.FootprintMunicipalities m
OUTER APPLY (
    SELECT TOP 1 s.Name AS HeldBy
    FROM opportunities.OpportunitySources s
    WHERE s.IsEnabled = 1 AND s.IsHistorical = 0 AND s.SourceType IN (2, 20, 21, 22)
      AND LEFT(s.Name, CHARINDEX(N'_', s.Name + N'_') - 1) = m.SourceNamePrefix
) h
WHERE m.Name = N'Port Moody';
GO
