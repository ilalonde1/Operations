/*
    Kor.OpportunitiesDb migration 276.

    CRM plan D2 (owner identity — the keystone). 2026-07-08.

    opportunities.BdStaff — a real staff directory that maps every spelling of a
    pursuit owner (the value stored in CrmEngagements.OwnerStaffId) to a mailbox
    and display name. Replaces the hand-typed MorningReportOwnerEmailMap env
    string that the per-owner digest used to route on.

    OwnerStaffId is heterogeneous by design/history:
      - grabbed pursuits store a UPN/email  (e.g. ilalonde@korstructural.com)
      - legacy BD-tracking rows store a first name (e.g. "Conor", "Jim")
      - win-backfill rows store NULL (BD name kept in Notes)

    So StaffKey holds whichever spelling the engagement carries; two spellings
    for one person are two rows pointing at the SAME Mailbox — and the digest
    already merges by resolved mailbox, so those collapse to one email.

    Population:
      - This migration SEEDS one row per distinct current OwnerStaffId. An
        email-shaped key self-routes (Mailbox = the key). A first-name key gets
        Mailbox = NULL, pending the Deltek EMMain sync (BdStaffDirectorySyncJob)
        which fills it from the authoritative directory on an UNAMBIGUOUS single
        first-name match — never a guess.

    Additive + idempotent (OBJECT_ID guard + NOT EXISTS seed). Reversible:
        DROP TABLE opportunities.BdStaff;
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'opportunities.BdStaff', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdStaff
    (
        Id           INT IDENTITY(1,1)                              NOT NULL,
        -- The owner spelling as stored on the engagement (UPN or first name).
        -- CI collation so "Jim"/"jim" are one identity and lookups are case-insensitive.
        StaffKey     NVARCHAR(150) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
        Mailbox      NVARCHAR(256)                                  NULL,  -- resolved email; NULL = unrouted (pending)
        DisplayName  NVARCHAR(200)                                  NULL,
        IsActive     BIT              NOT NULL CONSTRAINT DF_BdStaff_Active DEFAULT (1),
        Source       NVARCHAR(40)     NOT NULL CONSTRAINT DF_BdStaff_Source DEFAULT (N'seed'),
        CreatedAtUtc DATETIMEOFFSET   NOT NULL CONSTRAINT DF_BdStaff_Created DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc DATETIMEOFFSET   NOT NULL CONSTRAINT DF_BdStaff_Updated DEFAULT sysdatetimeoffset(),
        CONSTRAINT PK_BdStaff PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_BdStaff_StaffKey UNIQUE (StaffKey)
    );
END;
GO

-- Seed one row per distinct current pursuit owner. Idempotent: only inserts
-- keys not already present, so re-running never duplicates or clobbers a row
-- the sync has since enriched.
INSERT INTO opportunities.BdStaff (StaffKey, Mailbox, Source)
SELECT DISTINCT
       LTRIM(RTRIM(e.OwnerStaffId)),
       CASE WHEN e.OwnerStaffId LIKE N'%@%' THEN LTRIM(RTRIM(e.OwnerStaffId)) ELSE NULL END,
       CASE WHEN e.OwnerStaffId LIKE N'%@%' THEN N'engagement:self' ELSE N'engagement:name' END
FROM   opportunities.CrmEngagements e
WHERE  e.OwnerStaffId IS NOT NULL
  AND  LTRIM(RTRIM(e.OwnerStaffId)) <> N''
  AND  NOT EXISTS (
        SELECT 1 FROM opportunities.BdStaff s
        WHERE s.StaffKey = LTRIM(RTRIM(e.OwnerStaffId))
  );
GO

PRINT 'Migration 276 complete.';
GO
