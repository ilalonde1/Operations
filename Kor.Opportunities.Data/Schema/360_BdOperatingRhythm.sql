/*
    KorOpportunitiesDb migration 360: BD Director version 2 — the operating rhythm Ian committed to on 9 October 2026.

    On the 9 October BD call the team voted Ian in to manage BD. He committed to a short all-hands, a 20-minute one-to-one with
    each regional lead about every two weeks, actions every Monday, and tracking what each action produced. Jim asked for "an
    action list of all the things we've agreed we're going to do between now and when we meet next". The Fraser Health seismic
    list was denied on references.

      opportunities.BdMeeting     a meeting: all-hands, one-to-one or other; agenda and recap; where its record lives.
      opportunities.BdAsk         +MeetingId, +Region, +SourceRef. The asks ledger (348) IS the action register: owner, action,
                                  why, minutes, due, status, and the closing note is the result.
      opportunities.BdReference   every reference contact KOR gives out: which application, which project, which role, who,
                                  when last called and what they said. No CanonicalOrg FK, so BdCanonicalDedup's foreign-key
                                  guard needs no change (348 blocked every merge for a day by adding one).
      opportunities.DirectorHour  the prepared hour, written in Claude's session (rule 14: no model call at runtime) and
                                  imported by the CLI; the App shows the newest for today; the Worker emails it at 6 a.m.

    Seeds, once (guarded on the 9 October meeting row): that meeting, its 31 actions, and the 15 reference rows of the Fraser
    Health application exactly as submitted (Downloads\1-Application ...\1-1_-_Application_..._FILLED.pdf, Appendix C) — only
    3 of the 15 name a person.

    Nothing here sends anything to anyone.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'opportunities.BdMeeting', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdMeeting (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BdMeeting PRIMARY KEY CLUSTERED,
        Kind NVARCHAR(20) NOT NULL CONSTRAINT CK_BdMeeting_Kind CHECK (Kind IN (N'AllHands', N'OneToOne', N'Other')),
        Title NVARCHAR(200) NOT NULL,
        WithStaffKey NVARCHAR(150) NULL,
        ScheduledForUtc DATETIMEOFFSET NOT NULL,
        DurationMinutes INT NOT NULL CONSTRAINT DF_BdMeeting_Duration DEFAULT 27 CONSTRAINT CK_BdMeeting_Duration CHECK (DurationMinutes BETWEEN 5 AND 240),
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_BdMeeting_Status DEFAULT N'Planned'
            CONSTRAINT CK_BdMeeting_Status CHECK (Status IN (N'Planned', N'Held', N'Cancelled')),
        Attendees NVARCHAR(1000) NULL,
        Agenda NVARCHAR(MAX) NULL,
        Recap NVARCHAR(MAX) NULL,
        RecordRef NVARCHAR(500) NULL,
        CreatedBy NVARCHAR(150) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdMeeting_CreatedAt DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdMeeting_UpdatedAt DEFAULT sysdatetimeoffset()
    );
    CREATE INDEX IX_BdMeeting_ScheduledFor ON opportunities.BdMeeting (ScheduledForUtc);
END
GO

IF COL_LENGTH(N'opportunities.BdAsk', N'MeetingId') IS NULL
    ALTER TABLE opportunities.BdAsk ADD MeetingId BIGINT NULL CONSTRAINT FK_BdAsk_Meeting REFERENCES opportunities.BdMeeting(Id);
GO
IF COL_LENGTH(N'opportunities.BdAsk', N'Region') IS NULL
    ALTER TABLE opportunities.BdAsk ADD Region NVARCHAR(40) NULL;
GO
IF COL_LENGTH(N'opportunities.BdAsk', N'SourceRef') IS NULL
    ALTER TABLE opportunities.BdAsk ADD SourceRef NVARCHAR(200) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BdAsk_MeetingId' AND object_id = OBJECT_ID(N'opportunities.BdAsk'))
    CREATE INDEX IX_BdAsk_MeetingId ON opportunities.BdAsk (MeetingId);
GO

IF OBJECT_ID(N'opportunities.BdReference', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.BdReference (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BdReference PRIMARY KEY CLUSTERED,
        Application NVARCHAR(200) NOT NULL,
        Project NVARCHAR(300) NOT NULL,
        Role NVARCHAR(60) NOT NULL,
        Company NVARCHAR(200) NULL,
        ContactName NVARCHAR(200) NULL,
        Phone NVARCHAR(60) NULL,
        Email NVARCHAR(256) NULL,
        KorLead NVARCHAR(150) NULL,
        LastCalledUtc DATETIMEOFFSET NULL,
        CalledBy NVARCHAR(150) NULL,
        CallOutcome NVARCHAR(30) NOT NULL CONSTRAINT DF_BdReference_Outcome DEFAULT N'NotCalled'
            CONSTRAINT CK_BdReference_Outcome CHECK (CallOutcome IN (N'NotCalled', N'Confirmed', N'LeftMessage', N'NoLongerThere', N'Declined')),
        Notes NVARCHAR(1000) NULL,
        CreatedBy NVARCHAR(150) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdReference_CreatedAt DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_BdReference_UpdatedAt DEFAULT sysdatetimeoffset()
    );
END
GO

IF OBJECT_ID(N'opportunities.DirectorHour', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.DirectorHour (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DirectorHour PRIMARY KEY CLUSTERED,
        ForDate DATE NOT NULL,
        Kind NVARCHAR(20) NOT NULL CONSTRAINT DF_DirectorHour_Kind DEFAULT N'BD'
            CONSTRAINT CK_DirectorHour_Kind CHECK (Kind IN (N'BD', N'Operations')),
        ContentJson NVARCHAR(MAX) NOT NULL CONSTRAINT CK_DirectorHour_Json CHECK (ISJSON(ContentJson) = 1),
        WrittenBy NVARCHAR(150) NOT NULL,
        ImportedBy NVARCHAR(150) NOT NULL,
        ImportedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_DirectorHour_ImportedAt DEFAULT sysdatetimeoffset(),
        EmailedAtUtc DATETIMEOFFSET NULL
    );
    CREATE INDEX IX_DirectorHour_ForDate ON opportunities.DirectorHour (Kind, ForDate, ImportedAtUtc);
END
GO

-- ===== seeds, once =====
IF NOT EXISTS (SELECT 1 FROM opportunities.BdMeeting WHERE Title = N'BD Updates' AND ScheduledForUtc = '2026-10-09T21:01:00+00:00')
BEGIN
    BEGIN TRANSACTION;
    DECLARE @ian NVARCHAR(150) = N'ilalonde@korstructural.com';
    INSERT INTO opportunities.BdMeeting (Kind, Title, ScheduledForUtc, DurationMinutes, Status, Attendees, Recap, RecordRef, CreatedBy)
    VALUES (N'AllHands', N'BD Updates', '2026-10-09T21:01:00+00:00', 93, N'Held',
            N'Ian Lalonde, Jim DesRoches, John Markulin, Conor Murtagh, Islam Shabana, Omar Alcazar (Rory Beirne absent)',
            N'The team voted Ian in to manage business development. Ian committed to a short all-hands, a 20-minute one-to-one with each regional lead about every two weeks, actions every Monday, tracking what each action produced, a clean start for the CRM from the team spreadsheet, and a pilot of the app with Omar, Islam and Jim. The Fraser Health seismic list was denied on references; the team will vet one named, current contact per project and reapply in November.',
            N'Desktop\BD\Calls\BD Updates-20261009_140100-Meeting Recording.mp4 and BD Updates.docx',
            @ian);
    DECLARE @m BIGINT = SCOPE_IDENTITY();

    INSERT INTO opportunities.BdAsk (ToStaffKey, Ask, WhyNow, Minutes, DueUtc, AskedBy, AskedAtUtc, Status, MeetingId, Region, SourceRef)
    SELECT v.Owner, v.Ask, v.WhyNow, v.Minutes, v.Due, @ian, '2026-10-09T22:30:00+00:00', N'Open', @m, v.Region, v.SourceRef
    FROM (VALUES
      (@ian, N'Hold the Fraser Health reference meeting with John, Rory and Jim: one named, current contact per project, each phoned first and told KOR was BMZ.', N'The seismic list was denied on references; 12 of the 15 reference rows named no person.', 30, CAST('2026-10-17T00:00:00+00:00' AS DATETIMEOFFSET), N'Lists', N'9 Oct BD call, 7:02'),
      (@ian, N'Reapply to Fraser Health with the corrected references.', N'Fraser Health said to reapply in a few months with references in order; the window keeps moving.', 90, '2026-11-21T00:00:00+00:00', N'Lists', N'9 Oct BD call, 8:43'),
      (N'omara@korstructural.com', N'Contact the architects who run seismic retrofit work.', N'Omar passed the seismic course; the RFPs usually come through those architects.', 60, '2026-10-31T00:00:00+00:00', N'Vancouver/LowerMainland', N'9 Oct BD call, 13:00'),
      (@ian, N'Send the regional action sheets and the one-page how-to.', N'Promised on the call.', 15, '2026-10-10T00:00:00+00:00', NULL, N'9 Oct BD call, 23:58'),
      (@ian, N'Install the app for Omar, Islam and Jim, and walk them through it.', N'The pilot group agreed on the call.', 60, '2026-10-17T00:00:00+00:00', NULL, N'9 Oct BD call, 1:07:15'),
      (@ian, N'Purge the old CRM data and re-import fresh from the team spreadsheet.', N'Archived on 16 Oct (migration 359); the spreadsheet import is the last build step.', 60, '2026-10-31T00:00:00+00:00', NULL, N'9 Oct BD call, 26:40'),
      (@ian, N'Send the meeting recap and set the next meeting date.', N'Promised at the close of the call.', 20, '2026-10-14T00:00:00+00:00', NULL, N'9 Oct BD call, 1:06:52'),
      (@ian, N'Talk to Rory: Continuum Architecture, Island West Coast Developments, and the Starlight work on the Island.', N'Rory was absent and Jim''s Island contacts are waiting on him.', 20, '2026-10-17T00:00:00+00:00', N'VancouverIsland', N'9 Oct BD call, 1:14:23'),
      (N'jdesroches@korstructural.com', N'San Diego trip: Bosa inspection, the owner of the project KOR has an RFP out on, a BD dinner.', N'Ian was asked to help pick who else to see.', 120, '2026-10-17T00:00:00+00:00', N'USA', N'9 Oct BD call, 28:24'),
      (N'jdesroches@korstructural.com', N'LA conference with John and Griffin, San Diego site reviews, and a visit to MVE on Thursday 5 November.', N'Nothing new in the US pipeline after current work.', 120, '2026-11-06T00:00:00+00:00', N'USA', N'9 Oct BD call, 28:24'),
      (N'ishabana@korstructural.com', N'Meet two principals of the Edmonton sports-facility architects introduced through Chris at Ellison (name heard as TPD or TBD).', N'A warm introduction from Ellison.', 60, '2026-10-17T00:00:00+00:00', N'Alberta', N'9 Oct BD call, 31:57'),
      (N'ishabana@korstructural.com', N'Ask friends at Associated Engineering and Stantec whether their structural groups outsource.', N'John Bryson said they sometimes do.', 30, '2026-10-20T00:00:00+00:00', N'Alberta', N'9 Oct BD call, 38:51'),
      (N'ishabana@korstructural.com', N'Meet Turn''s CEO in Calgary about the proposal worth about $600,000 in fees; Turn plans a similar project in Ottawa.', N'The largest open proposal; the CEO is back mid-October.', 90, '2026-10-31T00:00:00+00:00', N'Alberta', N'9 Oct BD call, 39:56'),
      (N'omara@korstructural.com', N'Attend Bird''s appreciation night on 29 October, and see Turn the next morning if possible.', N'Bird invited KOR; Jim cannot attend.', 120, '2026-10-30T00:00:00+00:00', N'Alberta', N'9 Oct BD call, 40:08'),
      (N'ishabana@korstructural.com', N'Get involved with the Alberta construction association, through Ryan at Del Noel and Kerri.', N'Everyone in Alberta construction is in it, per Ryan.', 30, '2026-10-31T00:00:00+00:00', N'Alberta', N'9 Oct BD call, 1:17:13'),
      (N'jdesroches@korstructural.com', N'Find out how to get security clearance and who can sponsor KOR; Ellison has someone who knows the process, and Islam may already hold clearance.', N'Ellison: no clearance, no place on the team. It can take up to a year.', 60, '2026-10-24T00:00:00+00:00', N'Defence', N'9 Oct BD call, 36:03'),
      (@ian, N'Register for DND and DCC''s Building Defence 2026 industry days, virtually, under John''s name, and stream it on Teams.', N'Agreed on the call; virtual is about $400.', 20, '2026-11-07T00:00:00+00:00', N'Defence', N'9 Oct BD call, 1:28:46'),
      (@ian, N'Turn the DCC and DND contract award lists Jim circulated into a list of companies to approach.', N'Who has won defence work recently, and for how much.', 60, '2026-10-31T00:00:00+00:00', N'Defence', N'9 Oct BD call, 1:27:22'),
      (N'cmurtagh@korstructural.com', N'Put Omar, Rory and Conor on the new Kelowna project with the Mexican architect (name heard as Dualita), for Zen Core.', N'Show the architect a team, not one person; Omar holds the connection.', 30, '2026-10-24T00:00:00+00:00', N'Okanagan-BcInterior', N'9 Oct BD call, 44:43'),
      (N'jmarkulin@korstructural.com', N'Refer GM to West Group, and plug KOR with Brad Jones, Senior VP of Development there.', N'Brad called John; an easy opening.', 20, '2026-10-24T00:00:00+00:00', N'Okanagan-BcInterior', N'9 Oct BD call, 47:14'),
      (N'omara@korstructural.com', N'Follow up the people met at the Graham 100-year party, including an MCM principal (possibly Mark Thomson) who works with PCI Developments.', N'Fresh introductions cool quickly.', 45, '2026-10-24T00:00:00+00:00', N'Vancouver/LowerMainland', N'9 Oct BD call, 55:09'),
      (N'omara@korstructural.com', N'Reach Beedie about the two Surrey towers said to be starting soon.', N'Omar phoned and got no answer; follow up this week.', 15, '2026-10-17T00:00:00+00:00', N'Vancouver/LowerMainland', N'9 Oct BD call, 56:26'),
      (N'jmarkulin@korstructural.com', N'Chase the Surrey 10,000-seat rink: offer design-build input; try Tyler Pascal again.', N'A big rink would be a marquee job.', 20, '2026-10-17T00:00:00+00:00', N'Vancouver/LowerMainland', N'9 Oct BD call, 57:23'),
      (@ian, N'Find who leads Bird''s building division in Vancouver; the Bird dossier names Rob McGrath, EVP Buildings West.', N'Bird''s Vancouver work includes the VCC expansion at Clark and Great Northern Way.', 20, '2026-10-24T00:00:00+00:00', N'Vancouver/LowerMainland', N'9 Oct BD call, 1:01:14'),
      (N'jdesroches@korstructural.com', N'Show Omar the Heather Lands carbon method before the city''s carbon meeting on the Filipino hotel and cultural centre.', N'The project was approved; the city requires the carbon meeting.', 20, '2026-10-21T00:00:00+00:00', N'Vancouver/LowerMainland', N'9 Oct BD call, 1:05:32'),
      (N'rbeirne@korstructural.com', N'Contact Continuum Architecture''s Jeremy Beintema and Gregory Eeman, the architects on the two Trillium projects in Saanich.', N'KOR''s fee was 22 to 25 percent below three competitors; the owner asked RJC why.', 30, '2026-10-24T00:00:00+00:00', N'VancouverIsland', N'9 Oct BD call, 1:09:55'),
      (N'rbeirne@korstructural.com', N'Meet Paul Krzywicki at Island West Coast Developments in Nanaimo, with Jim.', N'Jim knows Paul well; Rory had been trying to get in.', 60, '2026-10-31T00:00:00+00:00', N'VancouverIsland', N'9 Oct BD call, 1:12:00'),
      (N'jmarkulin@korstructural.com', N'Decide on ICBA in Vancouver in November (about $600; Bird''s Terry McGibbon is on a panel).', N'Omar recommends it; the team said no to the EGBC AGM.', 10, '2026-10-31T00:00:00+00:00', N'Vancouver/LowerMainland', N'9 Oct BD call, 1:07:37'),
      (N'ishabana@korstructural.com', N'Post a KOR project on LinkedIn with Omar.', N'Posts drew reactions; none for three weeks.', 30, '2026-10-24T00:00:00+00:00', NULL, N'9 Oct BD call, 56:45'),
      (N'jdesroches@korstructural.com', N'Pick who to see on the two US trips that KOR does not already know.', N'Compare notes with Ian before travelling.', 30, '2026-10-17T00:00:00+00:00', N'USA', N'9 Oct BD call, 54:40'),
      (@ian, N'Plan next year''s events: Mass Timber in Portland in March, Build X in Calgary, and the defence industry days in person if this year is worth it.', N'Targeted, not just showing up.', 30, '2026-11-28T00:00:00+00:00', NULL, N'9 Oct BD call, 54:25')
    ) v(Owner, Ask, WhyNow, Minutes, Due, Region, SourceRef);
    SELECT @@ROWCOUNT AS actions_seeded;

    DECLARE @app NVARCHAR(200) = N'Fraser Health seismic list, July 2026 (denied on references)';
    INSERT INTO opportunities.BdReference (Application, Project, Role, Company, ContactName, Phone, Email, KorLead, Notes, CreatedBy)
    VALUES
      (@app, N'Guru Nanak / PICS Diversity Village, Surrey (125-bed long-term care)', N'Owner', N'PICS', N'Satbir Cheema', N'(604) 596-7722', N'satbir.cheema@pics.bc.ca', N'jmarkulin@korstructural.com', NULL, @ian),
      (@app, N'Guru Nanak / PICS Diversity Village, Surrey (125-bed long-term care)', N'Architect', N'Ciccozzi Architecture', N'Shannon Seefeldt', N'(604) 687-4741', N'shannon@ciccozziarchitecture.com', N'jmarkulin@korstructural.com', NULL, @ian),
      (@app, N'Guru Nanak / PICS Diversity Village, Surrey (125-bed long-term care)', N'General contractor / CM', N'Buttcon', NULL, N'(905) 907-4242', NULL, N'jmarkulin@korstructural.com', N'No person named; an Ontario switchboard number.', @ian),
      (@app, N'Belmont Village of La Jolla, San Diego (17-storey senior living)', N'Owner / developer', N'Belmont Village Senior Living / Greystar', NULL, NULL, NULL, N'jdesroches@korstructural.com', N'No person, phone or email given.', @ian),
      (@app, N'Belmont Village of La Jolla, San Diego (17-storey senior living)', N'Architect', N'JWDA (Joseph Wong Design Associates)', NULL, N'(619) 233-6777', NULL, N'jdesroches@korstructural.com', N'No person named.', @ian),
      (@app, N'Belmont Village of La Jolla, San Diego (17-storey senior living)', N'General contractor', N'Suffolk Construction (San Diego)', NULL, N'(619) 297-4156', NULL, N'jdesroches@korstructural.com', N'No person named; built when KOR was BMZ.', @ian),
      (@app, N'Dania Seniors Home, Burnaby', N'Owner', N'Dania Society', NULL, N'(604) 299-1370', N'info@dania.bc.ca', N'jmarkulin@korstructural.com', N'Generic inbox only.', @ian),
      (@app, N'Dania Seniors Home, Burnaby', N'Architect', N'NSDA Architects', N'Brian Dust', N'(604) 669-1926', NULL, N'jmarkulin@korstructural.com', NULL, @ian),
      (@app, N'Dania Seniors Home, Burnaby', N'General contractor', N'VanMar Constructors (BC)', NULL, N'(604) 882-0700', NULL, N'jmarkulin@korstructural.com', N'No person named.', @ian),
      (@app, N'Harrison Healthcare Clinic, 1280 Burrard, Vancouver (tenant improvement, $4,500 fee)', N'Owner', N'Harrison Healthcare', NULL, NULL, NULL, N'rbeirne@korstructural.com', N'No contact given.', @ian),
      (@app, N'Harrison Healthcare Clinic, 1280 Burrard, Vancouver (tenant improvement, $4,500 fee)', N'Architect / designer', N'Cutler Design & Architecture', NULL, N'(604) 681-5050', N'info@cutlerdc.com', N'rbeirne@korstructural.com', N'Generic inbox only.', @ian),
      (@app, N'Harrison Healthcare Clinic, 1280 Burrard, Vancouver (tenant improvement, $4,500 fee)', N'General contractor', NULL, NULL, NULL, NULL, N'rbeirne@korstructural.com', N'Left blank on the application.', @ian),
      (@app, N'Kings Crossing Personal Care Clinic, Burnaby (tenant improvement, $4,000 fee)', N'Owner', N'CDC Construction Ltd.', NULL, NULL, NULL, N'rbeirne@korstructural.com', N'No contact given.', @ian),
      (@app, N'Kings Crossing Personal Care Clinic, Burnaby (tenant improvement, $4,000 fee)', N'Architect', NULL, NULL, NULL, NULL, N'rbeirne@korstructural.com', N'Left blank on the application.', @ian),
      (@app, N'Kings Crossing Personal Care Clinic, Burnaby (tenant improvement, $4,000 fee)', N'General contractor', NULL, NULL, NULL, NULL, N'rbeirne@korstructural.com', N'Left blank on the application.', @ian);
    SELECT @@ROWCOUNT AS references_seeded;
    COMMIT TRANSACTION;
END
GO

SELECT (SELECT COUNT(*) FROM opportunities.BdMeeting) AS meetings, (SELECT COUNT(*) FROM opportunities.BdAsk) AS asks,
       (SELECT COUNT(*) FROM opportunities.BdReference) AS reference_rows,
       (SELECT COUNT(*) FROM opportunities.BdReference WHERE ContactName IS NOT NULL) AS named_references,
       (SELECT COUNT(*) FROM opportunities.DirectorHour) AS hours;
GO
