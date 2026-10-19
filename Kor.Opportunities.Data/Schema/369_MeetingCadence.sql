/*
    KorOpportunitiesDb migration 369: the meeting cadence, for both virtual seats (Ian, 18 October 2026).

    The virtual BD Manager and the virtual COO prepare Ian's meetings: a 27-minute BD monthly with the regional leads and JM, a
    27-minute COO monthly with JM and Jim DesRoches, and a biweekly quickie with each person (15 minutes; 20 for JM and Jim,
    whose one quickie covers BD and COO). Each due meeting becomes a move in the morning hour with its invite and pre-read as
    Outlook DRAFTS. Nothing is sent by the platform; Ian sends.

      opportunities.MeetingCadence   one row per recurring meeting: seat, who, how long, how often, the preferred slot, and
                                     the earliest first date (the BD monthly waits for 13 November: Jim is at MVE in LA the
                                     week before, action #10).
      opportunities.BdMeeting        +CadenceId: the cadence a planned or held meeting belongs to. The 9 October all-hands is
                                     linked to the BD monthly, so the cadence counts from it.

    Idempotent; GO-batched.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'opportunities.MeetingCadence', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.MeetingCadence (
        Id INT NOT NULL CONSTRAINT PK_MeetingCadence PRIMARY KEY CLUSTERED,
        Seat NVARCHAR(10) NOT NULL CONSTRAINT CK_MeetingCadence_Seat CHECK (Seat IN (N'BD', N'COO', N'Both')),
        Cadence NVARCHAR(10) NOT NULL CONSTRAINT CK_MeetingCadence_Cadence CHECK (Cadence IN (N'Monthly', N'Quickie')),
        MeetingKind NVARCHAR(20) NOT NULL CONSTRAINT CK_MeetingCadence_Kind CHECK (MeetingKind IN (N'AllHands', N'OneToOne', N'Other')),
        Title NVARCHAR(200) NOT NULL,
        WithStaffKey NVARCHAR(150) NULL,
        Attendees NVARCHAR(1000) NOT NULL,
        DurationMinutes INT NOT NULL CONSTRAINT CK_MeetingCadence_Duration CHECK (DurationMinutes BETWEEN 5 AND 240),
        EveryDays INT NOT NULL CONSTRAINT CK_MeetingCadence_Every CHECK (EveryDays BETWEEN 7 AND 92),
        PreferredWeekday INT NOT NULL CONSTRAINT CK_MeetingCadence_Weekday CHECK (PreferredWeekday BETWEEN 1 AND 5),
        PreferredLocalTime TIME(0) NOT NULL,
        FirstOnOrAfter DATE NULL,
        SortOrder INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_MeetingCadence_IsActive DEFAULT 1,
        CONSTRAINT CK_MeetingCadence_With CHECK (MeetingKind <> N'OneToOne' OR WithStaffKey IS NOT NULL)
    );
END
GO

IF COL_LENGTH(N'opportunities.BdMeeting', N'CadenceId') IS NULL
    ALTER TABLE opportunities.BdMeeting ADD CadenceId INT NULL CONSTRAINT FK_BdMeeting_Cadence REFERENCES opportunities.MeetingCadence(Id);
GO

-- Attendees are staff keys separated by ';'. Weekday 1 = Monday. Times are Vancouver local.
MERGE opportunities.MeetingCadence AS t
USING (VALUES
    (1, N'BD',   N'Monthly', N'AllHands', N'BD monthly',                 NULL,
        N'omara@korstructural.com;ishabana@korstructural.com;cmurtagh@korstructural.com;rbeirne@korstructural.com;jdesroches@korstructural.com;jmarkulin@korstructural.com',
        27, 28, 5, CAST('14:00' AS TIME(0)), CAST('2026-11-13' AS DATE), 10),
    (2, N'COO',  N'Monthly', N'Other',    N'Operations monthly',         NULL,
        N'jmarkulin@korstructural.com;jdesroches@korstructural.com',
        27, 28, 4, CAST('09:00' AS TIME(0)), CAST('2026-10-29' AS DATE), 20),
    (3, N'BD',   N'Quickie', N'OneToOne', N'BD quickie: Omar',           N'omara@korstructural.com',    N'omara@korstructural.com',    15, 14, 2, CAST('10:00' AS TIME(0)), CAST('2026-10-20' AS DATE), 30),
    (4, N'BD',   N'Quickie', N'OneToOne', N'BD quickie: Islam',          N'ishabana@korstructural.com', N'ishabana@korstructural.com', 15, 14, 2, CAST('10:30' AS TIME(0)), CAST('2026-10-20' AS DATE), 40),
    (5, N'BD',   N'Quickie', N'OneToOne', N'BD quickie: Conor',          N'cmurtagh@korstructural.com', N'cmurtagh@korstructural.com', 15, 14, 3, CAST('10:00' AS TIME(0)), CAST('2026-10-21' AS DATE), 50),
    (6, N'BD',   N'Quickie', N'OneToOne', N'BD quickie: Rory',           N'rbeirne@korstructural.com',  N'rbeirne@korstructural.com',  15, 14, 3, CAST('10:30' AS TIME(0)), CAST('2026-10-21' AS DATE), 60),
    (7, N'Both', N'Quickie', N'OneToOne', N'Quickie, BD and operations: JM',  N'jmarkulin@korstructural.com',  N'jmarkulin@korstructural.com',  20, 14, 4, CAST('10:00' AS TIME(0)), CAST('2026-10-29' AS DATE), 70),
    (8, N'Both', N'Quickie', N'OneToOne', N'Quickie, BD and operations: Jim', N'jdesroches@korstructural.com', N'jdesroches@korstructural.com', 20, 14, 4, CAST('10:30' AS TIME(0)), CAST('2026-10-29' AS DATE), 80)
) AS s (Id, Seat, Cadence, MeetingKind, Title, WithStaffKey, Attendees, DurationMinutes, EveryDays, PreferredWeekday, PreferredLocalTime, FirstOnOrAfter, SortOrder)
ON t.Id = s.Id
WHEN NOT MATCHED THEN INSERT (Id, Seat, Cadence, MeetingKind, Title, WithStaffKey, Attendees, DurationMinutes, EveryDays, PreferredWeekday, PreferredLocalTime, FirstOnOrAfter, SortOrder)
    VALUES (s.Id, s.Seat, s.Cadence, s.MeetingKind, s.Title, s.WithStaffKey, s.Attendees, s.DurationMinutes, s.EveryDays, s.PreferredWeekday, s.PreferredLocalTime, s.FirstOnOrAfter, s.SortOrder);
GO

-- The 9 October all-hands is the BD monthly's first meeting.
UPDATE opportunities.BdMeeting SET CadenceId = 1
WHERE CadenceId IS NULL AND Kind = N'AllHands' AND Title = N'BD Updates' AND CAST(ScheduledForUtc AS DATE) = '2026-10-09';
GO
