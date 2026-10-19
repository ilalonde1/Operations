/*
    KorOpportunitiesDb migration 371: pursuit touchstones (Ian, 18 October 2026, option A).

    Every live pursuit has an owner. At set points in its life (two days after a tender is grabbed, a week before it closes,
    two weeks after a proposal goes in, three weeks without a touch) the owner should hear what matters now. The morning hour
    gathers each owner's due touchstones into ONE weekly roll-up and writes it as an Outlook DRAFT addressed to the owner,
    opened unsent in Ian's Outlook. Ian forwards it or not; the platform sends nothing.

      opportunities.PursuitTouchstone       the rules: which pursuits (Tender = carries an opportunity, Relationship = BD
                                            tracking, Any), the anchor date, the offset in days, how long it stays due, and
                                            what the nudge must carry. Rules are data: change a row, not the code.
      opportunities.PursuitTouchstoneDraft  one row per touchstone drafted for a pursuit, keyed on the anchor date it was
                                            due from, so a recurring touchstone (keep it warm) can come due again later. The
                                            app writes it when Ian opens the draft in Outlook.

    Anchors: Opened (engagement opened), Deadline (the opportunity's submission deadline), Submitted (entered stage 3),
    LastTouch (latest of activity, filed email and the last keep-warm draft), NextAction (NextActionDueUtc), Won and Lost
    (closed in that stage). WindowDays 0 = stays due until drafted.

    Idempotent; GO-batched.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'opportunities.PursuitTouchstone', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PursuitTouchstone (
        Id INT NOT NULL CONSTRAINT PK_PursuitTouchstone PRIMARY KEY CLUSTERED,
        Applies NVARCHAR(20) NOT NULL CONSTRAINT CK_PursuitTouchstone_Applies CHECK (Applies IN (N'Tender', N'Relationship', N'Any')),
        Anchor NVARCHAR(20) NOT NULL CONSTRAINT CK_PursuitTouchstone_Anchor
            CHECK (Anchor IN (N'Opened', N'Deadline', N'Submitted', N'LastTouch', N'NextAction', N'Won', N'Lost')),
        OffsetDays INT NOT NULL CONSTRAINT CK_PursuitTouchstone_Offset CHECK (OffsetDays BETWEEN -60 AND 180),
        WindowDays INT NOT NULL CONSTRAINT CK_PursuitTouchstone_Window CHECK (WindowDays BETWEEN 0 AND 90),
        Name NVARCHAR(100) NOT NULL,
        Carries NVARCHAR(1000) NOT NULL,
        SortOrder INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_PursuitTouchstone_IsActive DEFAULT 1
    );
END
GO

-- The doctrine situations a touchstone matches (situation:<tag> on the cards), separated by ','.
IF COL_LENGTH(N'opportunities.PursuitTouchstone', N'Situations') IS NULL
    ALTER TABLE opportunities.PursuitTouchstone ADD Situations NVARCHAR(200) NOT NULL CONSTRAINT DF_PursuitTouchstone_Situations DEFAULT N'';
GO

IF OBJECT_ID(N'opportunities.PursuitTouchstoneDraft', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PursuitTouchstoneDraft (
        EngagementId BIGINT NOT NULL CONSTRAINT FK_PursuitTouchstoneDraft_Engagement REFERENCES opportunities.CrmEngagements(Id),
        TouchstoneId INT NOT NULL CONSTRAINT FK_PursuitTouchstoneDraft_Touchstone REFERENCES opportunities.PursuitTouchstone(Id),
        AnchorDate DATE NOT NULL,
        DraftedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_PursuitTouchstoneDraft_At DEFAULT SYSDATETIMEOFFSET(),
        DraftedBy NVARCHAR(150) NOT NULL,
        CONSTRAINT PK_PursuitTouchstoneDraft PRIMARY KEY CLUSTERED (EngagementId, TouchstoneId, AnchorDate)
    );
END
GO

MERGE opportunities.PursuitTouchstone AS t
USING (VALUES
    (1,  N'Tender',       N'Opened',     2,  5, N'Go / no-go',
        N'Bid or not, decided by a date. KOR''s history with this buyer, the fee against KOR''s win rate at that size, days to the deadline, and the checklist if one is loaded.', 10, N'rfp-response'),
    (2,  N'Tender',       N'Deadline',  -7,  4, N'Draft review',
        N'A week to close: the open checklist items by name, the team and references to name, the fee and how it compares, and who reviews the draft by when.', 20, N'rfp-response'),
    (3,  N'Tender',       N'Deadline',  -2,  2, N'Submission check',
        N'Two days to close: every open checklist item, who uploads, and confirm the submission time and method.', 30, N'rfp-response'),
    (4,  N'Tender',       N'Deadline',   1,  0, N'Deadline passed, still pending',
        N'The deadline has passed and the pursuit is still pending: was it submitted (record it and attach the file) or dropped (say why)?', 40, N'rfp-response'),
    (5,  N'Any',          N'Submitted', 14, 14, N'Follow up the submission',
        N'Two weeks after submitting: a short call to the buyer, what to ask (decision date, questions, shortlist), and one useful thing to offer.', 50, N'rfp-response,relationship-pursuit'),
    (6,  N'Any',          N'Submitted', 45,  0, N'Chase the decision',
        N'Six weeks without a decision: ask when it will be made; if it has been, ask for the debrief whichever way it went.', 60, N'rfp-response,relationship-pursuit'),
    (7,  N'Relationship', N'Opened',     7,  0, N'First touch',
        N'Never touched since it was opened (no activity or filed email): who to call, the reason to call now, and what KOR has done for them or people like them.', 70, N'never-touched,first-meeting'),
    (8,  N'Relationship', N'LastTouch', 21,  0, N'Keep it warm',
        N'Three weeks quiet: a fresh reason to call (their latest news, a KOR job they would care about, a code or market change), or a decision to stop.', 80, N'going-cold,relationship-pursuit'),
    (9,  N'Any',          N'NextAction', 0,  0, N'Your next step is due',
        N'The next action the owner set has come due: what it was, and whether it is done, moved or dropped.', 90, N'late-action'),
    (10, N'Any',          N'Won',        0, 30, N'Won: hand-off',
        N'Won and no Deltek project linked yet: open the project, name the PM, book the kickoff, and thank the client.', 100, N'won-pursuit'),
    (11, N'Any',          N'Lost',       0, 30, N'Lost: who won and why',
        N'Lost with no reason recorded: a close-out call to ask who won, on what (fee, team, relationship, approach), and how to be first next time.', 110, N'lost-pursuit')
) AS s (Id, Applies, Anchor, OffsetDays, WindowDays, Name, Carries, SortOrder, Situations)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET Applies = s.Applies, Anchor = s.Anchor, OffsetDays = s.OffsetDays, WindowDays = s.WindowDays,
    Name = s.Name, Carries = s.Carries, SortOrder = s.SortOrder, Situations = s.Situations
WHEN NOT MATCHED THEN INSERT (Id, Applies, Anchor, OffsetDays, WindowDays, Name, Carries, SortOrder, Situations)
    VALUES (s.Id, s.Applies, s.Anchor, s.OffsetDays, s.WindowDays, s.Name, s.Carries, s.SortOrder, s.Situations);
GO

SELECT Id, Applies, Anchor, OffsetDays, WindowDays, Name, Situations FROM opportunities.PursuitTouchstone ORDER BY SortOrder;
GO
