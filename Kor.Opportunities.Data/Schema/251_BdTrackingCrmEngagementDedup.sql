USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 251: BD audit 2026-06-19 C1.
  Collapse verified duplicate opportunities.CrmEngagements rows. Survivors and
  losers come from the Claude-verified map in docs/BD-Audit-2026-06-19.md.

  Repoints child rows first (CrmActivities, CrmContacts,
  CrmEngagementProjectLink), preserves loser Notes/PotentialProjects on the
  survivor, then deletes the loser engagement rows.
*/
BEGIN TRAN;

DECLARE @Map TABLE
(
    LoserId bigint NOT NULL PRIMARY KEY,
    SurvivorId bigint NOT NULL,
    Label nvarchar(100) NOT NULL
);

INSERT INTO @Map (LoserId, SurvivorId, Label)
VALUES
    (83, 44, N'DIALOG'),
    (85, 51, N'Greenstone'),
    (88, 64, N'JWDA Inc.'),
    (76, 31, N'Ledcor Omar'),
    (77, 35, N'Ledcor Islam'),
    (84, 50, N'Meiklejohn'),
    (74, 23, N'METAFOR'),
    (72, 11, N'Pinnacle Intl'),
    (82, 42, N'SAHURI'),
    (32, 63, N'Duke Mgmt AB-mis-regioned'),
    (75, 30, N'MAKE Projects Omar'),
    (78, 37, N'MAKE Projects Islam'),
    (79, 39, N'MAKE Projects Jim'),
    (90, 4,  N'RBI Group of Companies Omar');

PRINT 'Migration 251: engagement duplicate pairs staged: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE s
SET
    Notes = CASE
        WHEN NULLIF(LTRIM(RTRIM(l.Notes)), N'') IS NULL THEN s.Notes
        WHEN NULLIF(LTRIM(RTRIM(s.Notes)), N'') IS NULL THEN l.Notes
        WHEN CHARINDEX(CONVERT(nvarchar(4000), l.Notes), s.Notes) > 0 THEN s.Notes
        ELSE s.Notes + NCHAR(13) + NCHAR(10) + N'[m251 loser #' + CONVERT(nvarchar(20), l.Id) + N'] ' + l.Notes
    END,
    PotentialProjects = CASE
        WHEN NULLIF(LTRIM(RTRIM(l.PotentialProjects)), N'') IS NULL THEN s.PotentialProjects
        WHEN NULLIF(LTRIM(RTRIM(s.PotentialProjects)), N'') IS NULL THEN l.PotentialProjects
        WHEN CHARINDEX(CONVERT(nvarchar(4000), l.PotentialProjects), s.PotentialProjects) > 0 THEN s.PotentialProjects
        ELSE s.PotentialProjects + N' | ' + l.PotentialProjects
    END,
    ProposalsSubmittedCad = COALESCE(s.ProposalsSubmittedCad, l.ProposalsSubmittedCad),
    ProposalsAcceptedCad = COALESCE(s.ProposalsAcceptedCad, l.ProposalsAcceptedCad),
    UpdatedAtUtc = sysdatetimeoffset(),
    UpdatedBy = N'm251 BD audit engagement dedup'
FROM opportunities.CrmEngagements s
JOIN @Map mp ON mp.SurvivorId = s.Id
JOIN opportunities.CrmEngagements l ON l.Id = mp.LoserId;
PRINT 'Survivor engagement notes/potential/financial fields preserved: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE loserLink
FROM opportunities.CrmEngagementProjectLink loserLink
JOIN @Map mp ON mp.LoserId = loserLink.EngagementId
WHERE EXISTS
(
    SELECT 1
    FROM opportunities.CrmEngagementProjectLink survivorLink
    WHERE survivorLink.EngagementId = mp.SurvivorId
      AND survivorLink.MajorProjectsInventoryId = loserLink.MajorProjectsInventoryId
);
PRINT 'Duplicate CrmEngagementProjectLink rows removed before repoint: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE link
SET EngagementId = mp.SurvivorId,
    UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CrmEngagementProjectLink link
JOIN @Map mp ON mp.LoserId = link.EngagementId;
PRINT 'CrmEngagementProjectLink rows repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE act
SET EngagementId = mp.SurvivorId
FROM opportunities.CrmActivities act
JOIN @Map mp ON mp.LoserId = act.EngagementId;
PRINT 'CrmActivities rows repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE c
SET EngagementId = mp.SurvivorId,
    UpdatedAtUtc = sysdatetimeoffset(),
    UpdatedBy = N'm251 BD audit engagement dedup'
FROM opportunities.CrmContacts c
JOIN @Map mp ON mp.LoserId = c.EngagementId;
PRINT 'CrmContacts rows repointed: ' + CONVERT(varchar(20), @@ROWCOUNT);

DELETE e
FROM opportunities.CrmEngagements e
JOIN @Map mp ON mp.LoserId = e.Id;
PRINT 'Loser CrmEngagements deleted: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 251 complete: verified BD-tracking duplicate engagements collapsed.';
COMMIT TRAN;
GO
