/*
    KorOpportunitiesDb migration 361: BD Director, three screens — who the team is, and what Ian did with each move.

    Ian, 16 October 2026, on version 2: "v1 was brutal"; the mockup of Today / People / Pursuits was "a real good start. Build it".
    Two facts the screens need are not stored anywhere yet:

      opportunities.BdLead            the BD team as the 9 October call set it: each person's role (Manager, Lead, Partner) and
                                      region. BdStaff has no region column; the People screen reads this instead of hard-coding
                                      names. Seeded with the seven people who own actions on the register.
      opportunities.DirectorHourMove  what Ian did with each move of a prepared hour: Done, Not today or Not doing, when and by
                                      whom. One row per (hour, move); a second answer replaces the first.

    Nothing here sends anything to anyone. Creates are guarded; the seed inserts only missing people.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'opportunities.BdLead', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdLead (
        StaffKey NVARCHAR(150) NOT NULL CONSTRAINT PK_BdLead PRIMARY KEY CLUSTERED,
        Role NVARCHAR(20) NOT NULL CONSTRAINT CK_BdLead_Role CHECK (Role IN (N'Manager', N'Lead', N'Partner')),
        Region NVARCHAR(60) NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_BdLead_SortOrder DEFAULT 100,
        IsActive BIT NOT NULL CONSTRAINT DF_BdLead_IsActive DEFAULT 1,
        UpdatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdLead_UpdatedAt DEFAULT sysdatetimeoffset()
    );
END
GO

IF OBJECT_ID(N'opportunities.DirectorHourMove', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.DirectorHourMove (
        HourId BIGINT NOT NULL CONSTRAINT FK_DirectorHourMove_Hour REFERENCES opportunities.DirectorHour (Id),
        MoveRank INT NOT NULL CONSTRAINT CK_DirectorHourMove_Rank CHECK (MoveRank BETWEEN 1 AND 7),
        Result NVARCHAR(20) NOT NULL CONSTRAINT CK_DirectorHourMove_Result CHECK (Result IN (N'Done', N'NotToday', N'NotDoing')),
        Note NVARCHAR(500) NULL,
        RecordedBy NVARCHAR(150) NOT NULL,
        RecordedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_DirectorHourMove_At DEFAULT sysdatetimeoffset(),
        CONSTRAINT PK_DirectorHourMove PRIMARY KEY CLUSTERED (HourId, MoveRank)
    );
END
GO

-- The team as the 9 October call set it. Only people with an active BdStaff row; only those not already present.
INSERT INTO opportunities.BdLead (StaffKey, Role, Region, SortOrder)
SELECT v.StaffKey, v.Role, v.Region, v.SortOrder
FROM (VALUES
    (N'ilalonde@korstructural.com',   N'Manager', NULL,                      10),
    (N'omara@korstructural.com',      N'Lead',    N'Vancouver/LowerMainland', 20),
    (N'ishabana@korstructural.com',   N'Lead',    N'Alberta',                 30),
    (N'cmurtagh@korstructural.com',   N'Lead',    N'Okanagan-BcInterior',     40),
    (N'rbeirne@korstructural.com',    N'Lead',    N'VancouverIsland',         50),
    (N'jdesroches@korstructural.com', N'Lead',    N'USA',                     60),
    (N'jmarkulin@korstructural.com',  N'Partner', NULL,                       70)
) v (StaffKey, Role, Region, SortOrder)
WHERE EXISTS (SELECT 1 FROM opportunities.BdStaff s WHERE s.StaffKey = v.StaffKey AND s.IsActive = 1)
  AND NOT EXISTS (SELECT 1 FROM opportunities.BdLead l WHERE l.StaffKey = v.StaffKey);
SELECT @@ROWCOUNT AS leads_added;
GO

-- check
SELECT l.StaffKey, s.DisplayName, l.Role, l.Region FROM opportunities.BdLead l
JOIN opportunities.BdStaff s ON s.StaffKey = l.StaffKey ORDER BY l.SortOrder;
GO
