/*
    KorOpportunitiesDb migration 372: the operating plan (Ian, 19 October 2026).

    "The plan isn't bullshit - it's live and is the backbone for what informs our daily ops/bd/it plan - so it knows
    macro/micro." The four-part plan (strategy, the process from signal to cash, the rhythm, the platform) lives here as
    records, so the Director's Plan tab shows it and its PDF is printed from that same page.

      opportunities.PlanStage    the eight stages from signal to cash, plus IT underneath: owner, what happens, what the
                                 system does, the weak point. Pillar BD, Ops or IT.
      opportunities.PlanGoal     goals to a date: the metric that measures each (a key the plan reads, see below), baseline,
                                 target, and Status: Recommended until Ian decides it, then Decided (who, when), or Retired.
      opportunities.PlanPoint    the strategy text: where to play, how KOR wins, where it hurts, priorities, decisions owed.
      opportunities.PlanRhythm   the daily hour, roll-ups, quickies, monthlies and the quarterly review, each with the goal it
                                 moves and the stages it covers.
      opportunities.PlanPillar   the platform: BD apps, Financials, Operations, IT. What a Fortune 500 company has, what KOR
                                 has, what it found, what is next.

    Numbers in the text are written as {Metric} and filled from the day's readings when the page is drawn (PlanMetrics in
    Kor.Opportunities.Data/Plan): the scoreboard's ImpactSnapshot rows, and live CRM and ingestion counts. A metric with no
    reading yet prints as a dash, never a stale number. Figures from the dated win/loss analysis (72% decided, 43% of sent,
    266 never closed, since 2020) are written as text because they are a finding about the past, not a daily reading.

    The seed is the plan as drafted on 19 October; MERGE updates text but never touches a goal's Status, decision or target
    once Ian has decided it. Idempotent; GO-batched.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'opportunities.PlanStage', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PlanStage (
        No INT NOT NULL CONSTRAINT PK_PlanStage PRIMARY KEY CLUSTERED,
        Pillar NVARCHAR(10) NOT NULL CONSTRAINT CK_PlanStage_Pillar CHECK (Pillar IN (N'BD', N'Ops', N'IT')),
        Name NVARCHAR(60) NOT NULL,
        Owner NVARCHAR(300) NOT NULL,
        What NVARCHAR(500) NOT NULL,
        Today NVARCHAR(600) NOT NULL,
        SystemText NVARCHAR(600) NOT NULL,
        WeakPoint NVARCHAR(600) NOT NULL,
        Health NVARCHAR(10) NOT NULL CONSTRAINT CK_PlanStage_Health CHECK (Health IN (N'Good', N'Watch', N'Broken'))
    );
END
GO

IF OBJECT_ID(N'opportunities.PlanGoal', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PlanGoal (
        Id INT NOT NULL CONSTRAINT PK_PlanGoal PRIMARY KEY CLUSTERED,
        Pillar NVARCHAR(10) NOT NULL CONSTRAINT CK_PlanGoal_Pillar CHECK (Pillar IN (N'BD', N'Ops', N'IT')),
        StageNo INT NOT NULL CONSTRAINT FK_PlanGoal_Stage REFERENCES opportunities.PlanStage (No),
        Title NVARCHAR(200) NOT NULL,
        Metric NVARCHAR(60) NULL,
        BaselineValue FLOAT NULL,
        TargetValue FLOAT NULL,
        TargetText NVARCHAR(200) NOT NULL,
        DueDate DATE NOT NULL,
        Status NVARCHAR(12) NOT NULL CONSTRAINT DF_PlanGoal_Status DEFAULT N'Recommended'
            CONSTRAINT CK_PlanGoal_Status CHECK (Status IN (N'Recommended', N'Decided', N'Retired')),
        DecidedAtUtc DATETIMEOFFSET NULL,
        DecidedBy NVARCHAR(100) NULL,
        SortOrder INT NOT NULL
    );
END
GO

IF OBJECT_ID(N'opportunities.PlanPoint', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PlanPoint (
        Id INT NOT NULL CONSTRAINT PK_PlanPoint PRIMARY KEY CLUSTERED,
        Pillar NVARCHAR(10) NOT NULL CONSTRAINT CK_PlanPoint_Pillar CHECK (Pillar IN (N'BD', N'Ops', N'IT', N'All')),
        Section NVARCHAR(30) NOT NULL CONSTRAINT CK_PlanPoint_Section
            CHECK (Section IN (N'WhereToPlay', N'HowWeWin', N'WhereItHurts', N'Priorities', N'Decide', N'LeadGets', N'LeadAsk', N'Lead', N'Example')),
        Title NVARCHAR(200) NOT NULL,
        Detail NVARCHAR(800) NOT NULL,
        SortOrder INT NOT NULL
    );
END
GO

IF OBJECT_ID(N'opportunities.PlanRhythm', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PlanRhythm (
        Id INT NOT NULL CONSTRAINT PK_PlanRhythm PRIMARY KEY CLUSTERED,
        Name NVARCHAR(60) NOT NULL,
        WhoWhen NVARCHAR(300) NOT NULL,
        WhatHappens NVARCHAR(400) NOT NULL,
        Moves NVARCHAR(300) NOT NULL,
        StageFrom INT NOT NULL,
        StageTo INT NOT NULL,
        SortOrder INT NOT NULL
    );
END
GO

IF OBJECT_ID(N'opportunities.PlanPillar', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PlanPillar (
        PillarKey NVARCHAR(10) NOT NULL CONSTRAINT PK_PlanPillar PRIMARY KEY CLUSTERED,
        Title NVARCHAR(60) NOT NULL,
        Fortune500 NVARCHAR(400) NOT NULL,
        KorHas NVARCHAR(800) NOT NULL,
        Found NVARCHAR(600) NOT NULL,
        NextText NVARCHAR(400) NOT NULL,
        SortOrder INT NOT NULL
    );
END
GO

MERGE opportunities.PlanStage AS t
USING (VALUES
    (1, N'BD', N'Spot it', N'The software finds it; the regional lead picks the good ones.',
        N'New projects, permits, rezonings and tenders, and news about the people KOR works with.',
        N'{Signals30d} new in 30 days, from {SourcesLive} of {SourcesTotal} sources, in the app for every lead.',
        N'The app checks every source each night, keeps what is about buildings, and keeps a file on each client.',
        N'A tender is late news: by the time it is posted, the structural engineer is often already chosen. Permits and rezonings are where KOR gets in first.',
        N'Watch'),
    (2, N'BD', N'Follow it up', N'The regional lead: Omar, Islam, Conor, Rory, Jim; JM.',
        N'A real project or client someone is working on, with a named person and a next step.',
        N'{PursuitsLive} live; {PursuitsWithFee} carry a fee; {PursuitsUntouched14} untouched for 14 days or more.',
        N'The opportunity list in the app, and each lead''s weekly to-do list: first contact within 7 days, back in touch every 21 days, the next step on the day it is due.',
        N'Few calls and meetings are recorded in the app, so the to-do list cannot tell who has already been contacted.',
        N'Broken'),
    (3, N'BD', N'Write the proposal', N'The lead, with the engineer who will run the job.',
        N'Decide whether to bid, then write the fee proposal (filed in Project Opportunities\Pending).',
        N'{PursuitsTender} live tenders being chased, each with a bid or no-bid decision on record.',
        N'For tenders: bid or no-bid 2 days after it is posted, draft review a week before the deadline, a final check 2 days before. The bid documents and every addendum are saved with the opportunity automatically.',
        N'Claude''s RFP review runs from Ian''s PC, so a tender grabbed while he is away can wait a day for it; the bid documents are there at once.',
        N'Watch'),
    (4, N'BD', N'Get an answer', N'The lead.',
        N'Following up with the client until they say yes or no.',
        N'{SubmittedOver60} proposals sent more than 60 days ago with no answer recorded.',
        N'Follow up 14 days after sending, chase the decision at 45 days, both on the lead''s weekly to-do list.',
        N'Since 2020, 266 proposals were never marked won or lost, so KOR does not yet know its real win rate.',
        N'Broken'),
    (5, N'BD', N'Won or lost', N'The lead, handing over to the project manager.',
        N'Won: hand the job to the project team. Lost: find out who won, and why.',
        N'Of 291 lost or unanswered proposals since 2020 with a comment, fewer than 30 say why.',
        N'Within 30 days: the hand-off for a win, who won and why for a loss, both on the weekly to-do list.',
        N'Without the reasons for losses, KOR cannot learn what wins work.',
        N'Watch'),
    (6, N'Ops', N'Do the work', N'The project manager; JM and Jim oversee.',
        N'Designing the job within its fee.',
        N'{OverBudgetProjects} of {ActiveProjects} active projects over their hours budget, {OverBudgetHours} past budget in all; {CriticalProjects} critical; {LosingProjects} behind on billing or losing after overhead ({LosingProjectsLoss}), of which {LosingDoneProjects} are 90%+ billed and lost {LosingDoneProjectsLoss}; utilization {Exec.Utilization}.',
        N'The Financials screens in the app, the monthly Operations meeting, and the project items in Ian''s daily planning hour.',
        N'Proposed: an extra-service letter before a project passes its hours budget, tracked as a step, not found afterwards.',
        N'Broken'),
    (7, N'Ops', N'Bill it', N'The project manager.',
        N'Billing what has been earned, on time.',
        N'{InvoicedMonthAvg6} a month over the last six months, against {InvoicedMonthAvgPrior6} the six before; {BillingLagProjects} projects bill slower than they burn hours.',
        N'The list of projects billing slower than they burn hours, in Financials and in Ian''s daily planning hour.',
        N'Proposed: that list is reviewed at the monthly Operations meeting, not only read by Ian.',
        N'Broken'),
    (8, N'Ops', N'Get paid', N'A partner chases every invoice unpaid after 90 days (proposed).',
        N'Collecting what has been billed.',
        N'{Exec.DaysSalesOutstanding} days to collect; cash {Exec.CashPosition}; {ArTotal} owed; {ArOver90} over 90 days; {ArInactive} on projects no longer active.',
        N'The app records what is owed every morning; the Collections screen in Financials keeps a case for each overdue account; the monthly Operations meeting lists the largest.',
        N'Nobody is assigned to chase overdue invoices yet.',
        N'Broken'),
    (9, N'IT', N'Computers and IT', N'Ian.',
        N'Every computer and server working, up to date and backed up, underneath every step.',
        N'{ItCriticalGroups} critical issue groups, {ItWarningGroups} warnings; {ItUpdatesDue} machines with security updates due.',
        N'NetworkOps, KOR''s own IT monitoring: the health of every PC and server, the server rack, battery backup and firewall, updates, and fixes done remotely.',
        N'Outside IT spend should come down to antivirus, Veeam, Office 365, connectivity and phones.',
        N'Watch')
) AS s (No, Pillar, Name, Owner, What, Today, SystemText, WeakPoint, Health)
ON t.No = s.No
WHEN MATCHED THEN UPDATE SET Pillar = s.Pillar, Name = s.Name, Owner = s.Owner, What = s.What, Today = s.Today,
    SystemText = s.SystemText, WeakPoint = s.WeakPoint, Health = s.Health
WHEN NOT MATCHED THEN INSERT (No, Pillar, Name, Owner, What, Today, SystemText, WeakPoint, Health)
    VALUES (s.No, s.Pillar, s.Name, s.Owner, s.What, s.Today, s.SystemText, s.WeakPoint, s.Health);
GO

MERGE opportunities.PlanGoal AS t
USING (VALUES
    (1, N'BD', 2, N'Every opportunity has an estimated fee', N'PursuitsWithFee', 0.0, NULL, N'every live opportunity', 10),
    (2, N'BD', 2, N'No opportunity goes two weeks without contact', N'PursuitsUntouched14', 61.0, 0.0, N'none', 20),
    (3, N'BD', 4, N'No proposal waits more than 60 days for an answer', N'SubmittedOver60', NULL, 0.0, N'none', 30),
    (4, N'BD', 5, N'Every proposal marked won or lost, with who won and why', NULL, NULL, NULL, N'every proposal sent from now on', 40),
    (5, N'BD', 1, N'The app in the regional leads'' hands', NULL, NULL, NULL, N'every regional lead, with a one-page how-to', 50),
    (6, N'Ops', 7, N'Invoice over $400k a month again', N'InvoicedMonthAvg6', 367000.0, 400000.0, N'$400k a month', 60),
    (7, N'Ops', 8, N'Get paid faster: within 120 days on average', N'Exec.DaysSalesOutstanding', NULL, 120.0, N'120 days', 70),
    (8, N'Ops', 8, N'Less than $700k owed for more than 90 days', N'ArOver90', NULL, 700000.0, N'under $700k', 80),
    (9, N'Ops', 8, N'A decision on money owed for old, closed projects', N'ArInactive', NULL, NULL, N'each collected, settled or written off, with the reason', 90),
    (10, N'Ops', 6, N'Fewer projects over their hours; extra fees on the worst ten', N'OverBudgetProjects', NULL, NULL, N'down from where we started (18 Oct)', 100),
    (11, N'IT', 9, N'Every computer has this month''s security updates', N'ItUpdatesDue', NULL, 0.0, N'none due', 110),
    (12, N'IT', 9, N'No serious IT problem left open', N'ItCriticalGroups', NULL, 0.0, N'none', 120),
    (13, N'IT', 9, N'Outside IT costs only for antivirus, backups, Office 365, internet and phones', NULL, NULL, NULL, N'nothing else', 130),
    (14, N'Ops', 6, N'Stop losing money on finished projects; claim extra fees on the worst', N'LosingDoneProjectsLoss', NULL, NULL, N'down from where we started (18 Oct)', 95)
) AS s (Id, Pillar, StageNo, Title, Metric, BaselineValue, TargetValue, TargetText, SortOrder)
ON t.Id = s.Id
WHEN MATCHED AND t.Status = N'Recommended' THEN UPDATE SET Pillar = s.Pillar, StageNo = s.StageNo, Title = s.Title,
    Metric = s.Metric, BaselineValue = s.BaselineValue, TargetValue = s.TargetValue, TargetText = s.TargetText,
    SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Pillar, StageNo, Title, Metric, BaselineValue, TargetValue, TargetText, DueDate, SortOrder)
    VALUES (s.Id, s.Pillar, s.StageNo, s.Title, s.Metric, s.BaselineValue, s.TargetValue, s.TargetText, '2027-03-31', s.SortOrder);
GO

-- 20 October: the regional leads' section (what every lead gets, what Ian asks of them, one line per lead) joins the
-- sections a point may belong to. The table already exists live, so its check is widened in place.
-- Same day: section "Example" names the pursuit shown end to end as the plan's worked example (Detail = its pursuit id).
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_PlanPoint_Section'
           AND definition NOT LIKE N'%Example%')
BEGIN
    ALTER TABLE opportunities.PlanPoint DROP CONSTRAINT CK_PlanPoint_Section;
    ALTER TABLE opportunities.PlanPoint ADD CONSTRAINT CK_PlanPoint_Section
        CHECK (Section IN (N'WhereToPlay', N'HowWeWin', N'WhereItHurts', N'Priorities', N'Decide', N'LeadGets', N'LeadAsk', N'Lead', N'Example'));
END
GO

MERGE opportunities.PlanPoint AS t
USING (VALUES
    (1, N'BD', N'WhereToPlay', N'Grow the developers who already choose KOR.', N'Westland, Northland, Cressey, Domus, Kerkhoff and Polygon keep hiring KOR. Each gets a named partner and a standing check-in, not a proposal-by-proposal relationship.', 10),
    (2, N'BD', N'WhereToPlay', N'Metro Vancouver and South of Fraser are the base.', N'Where most of KOR''s work has come from since 2020.', 20),
    (3, N'BD', N'WhereToPlay', N'The US is the growth market.', N'Jim leads it: {PursuitsUsa} live US opportunities.', 30),
    (4, N'BD', N'WhereToPlay', N'Decide whether to bid before writing a fee for clients who rarely hire us.', N'NSDA, Wesgroup, Strand, Starlight and Porte have had many proposals from KOR and few jobs. Decide before the proposal, not after.', 40),
    (5, N'BD', N'HowWeWin', N'Get in as the architect''s engineer before the RFP.', N'The structural engineer is usually chosen at the start of schematic design.', 50),
    (7, N'BD', N'HowWeWin', N'Get an answer on every proposal.', N'Follow up at 14 days, chase the decision at 45, and record who won and why. Since 2020, 266 proposals were never marked won or lost, so KOR does not yet know its real win rate.', 70),
    (8, N'BD', N'HowWeWin', N'Spread the selling.', N'One principal has led half of KOR''s proposals since 2020 (345 of 701). The {PursuitsLive} live opportunities are now held by {PursuitOwners} people.', 80),
    (22, N'Ops', N'WhereItHurts', N'The bottom line.', N'Net income {Exec.NetIncomeT12mo} over 12 months on a labour margin of {Exec.LaborMarginT12mo}; net multiplier {Exec.NetMultiplier}. {LosingDoneProjects} projects that are 90%+ billed lost {LosingDoneProjectsLoss} after overhead: money given away. By fee type: {LosingDoneFixedFeeLoss} on {LosingDoneFixedFee} fixed-fee jobs (fee or scope), {LosingDoneMixedLoss} on {LosingDoneMixed} fixed-fee jobs with hourly extras, {LosingDoneHourlyLoss} on {LosingDoneHourly} hourly jobs (rates or write-offs). {LosingProjects} more read as losing on billing to date ({LosingProjectsLoss}), many only because billing lags the hours; {ProjectsBelowMultiplier2} are below a 2.0 multiplier.', 85),
    (9, N'Ops', N'WhereItHurts', N'Cash.', N'{ArTotal} owed, {ArOver90} of it over 90 days, and {ArInactive} on projects no longer on the active list.', 90),
    (10, N'Ops', N'WhereItHurts', N'Overruns.', N'{OverBudgetProjects} of {ActiveProjects} active projects are over their hours budget, {OverBudgetHours} past budget in all, given away unless charged; {CriticalProjects} are critical; {BillingLagProjects} are burning hours faster than they bill.', 100),
    (11, N'Ops', N'WhereItHurts', N'Capacity.', N'Utilization is {Exec.Utilization} over the last 30 days.', 110),
    (12, N'Ops', N'Priorities', N'Collect.', N'A partner owns every invoice over 90 days and reports at the monthly Operations meeting. Decide the money on inactive projects: collect, settle or write off.', 120),
    (13, N'Ops', N'Priorities', N'Charge for changes before the hours are spent.', N'Extra-service letters on the worst ten over-budget projects first.', 130),
    (14, N'Ops', N'Priorities', N'Bill on time.', N'Close the gap on the projects where billing lags the hours.', 140),
    (15, N'Ops', N'Priorities', N'Fix the process before January.', N'The January rush should land on a working routine, not a backlog.', 150),
    (16, N'IT', N'Priorities', N'Patch on schedule.', N'Every machine on the month''s security updates; {ItUpdatesDue} have updates due today.', 160),
    (17, N'IT', N'Priorities', N'Own the tools, keep the cost line.', N'Because of NetworkOps, outside IT spend comes down to antivirus, Veeam backups, Office 365, connectivity and phones.', 170),
    (18, N'All', N'Decide', N'The goals and their targets.', N'The figures are starting points from KOR''s record, not commitments, until Ian and the partners agree them.', 180),
    (19, N'All', N'Decide', N'Which repeat clients each partner looks after,', N'and who decides whether to bid for clients who rarely hire us.', 190),
    (20, N'All', N'Decide', N'What to do with money owed on old, closed projects.', N'{ArInactive} owed today on projects no longer active.', 200),
    (21, N'All', N'Decide', N'Whether more than one person works on the US.', N'One person sells the US today.', 210),
    (30, N'BD', N'LeadGets', N'New opportunities in their region.', N'Live. New tenders, permits and client news in the app, ranked best first.', 300),
    (31, N'BD', N'LeadGets', N'A short weekly to-do list.', N'Live. Who to call, what to send and by when, for each of their opportunities, sent by Ian; they reply one line per item.', 310),
    (32, N'BD', N'LeadGets', N'Reminders at the right time.', N'Live. In the weekly list: when to make first contact, when to follow up after sending a proposal, when to ask for a decision.', 320),
    (33, N'BD', N'LeadGets', N'Bid documents, and who else is bidding.', N'Live. The bid package of every Bids&Tenders and Alberta Purchasing Connection tender we pursue is saved with the opportunity minutes after it is grabbed, and checked daily for addenda until it closes.', 330),
    (34, N'BD', N'LeadGets', N'Help writing proposals.', N'Live. Claude reads the RFP of every tender we pursue: bid or not and why, the questions for the buyer, and a checklist of everything the client asked for; with the rate sheet and the team chart. First used on Leduc.', 340),
    (35, N'BD', N'LeadGets', N'Ian''s backing, from the week of 26 Oct.', N'Starting. From the week of 26 Oct: a quick answer on whether to bid and on the fee, and a 15-minute check-in every two weeks.', 350),
    (40, N'BD', N'LeadAsk', N'Keep every opportunity moving.', N'Each one has a next step and a date. If it is dead, say so and drop it.', 400),
    (41, N'BD', N'LeadAsk', N'Reply to the weekly list.', N'One line per item: done, new date, or dropped and why.', 410),
    (42, N'BD', N'LeadAsk', N'Decide whether to bid before writing a fee.', N'Especially for clients who rarely hire us.', 420),
    (43, N'BD', N'LeadAsk', N'Get an answer on every proposal.', N'Yes or no, and if no, who won and why.', 430),
    (44, N'BD', N'LeadAsk', N'Hand won jobs over properly.', N'Fee and scope agreed with the project manager before work starts.', 440),
    (50, N'BD', N'Lead', N'Omar · Vancouver', N'Metro Vancouver and South of Fraser, KOR''s base, and the repeat developers who already choose KOR.', 500),
    (51, N'BD', N'Lead', N'Islam · Alberta', N'Public and institutional work across Alberta, much of it tendered on the Alberta Purchasing Connection, and the developers in Calgary and Edmonton.', 510),
    (52, N'BD', N'Lead', N'Conor · Okanagan', N'Kelowna and the Okanagan: developers, municipalities and institutions.', 520),
    (53, N'BD', N'Lead', N'Rory · Vancouver Island', N'Victoria, Nanaimo and the Island: developers, BC Housing projects and the municipalities.', 530),
    (54, N'BD', N'Lead', N'Jim · United States', N'KOR''s growth market: the US opportunities and the architects who bring KOR in.', 540),
    (60, N'All', N'Example', N'Leduc West Campus Recreation Centre, structural consultant', N'1112', 600)
) AS s (Id, Pillar, Section, Title, Detail, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET Pillar = s.Pillar, Section = s.Section, Title = s.Title, Detail = s.Detail, SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Pillar, Section, Title, Detail, SortOrder)
    VALUES (s.Id, s.Pillar, s.Section, s.Title, s.Detail, s.SortOrder);
GO

-- 20 October: point 6 ("mid-size fees convert best") rested on win rates from the fee-proposal tracker, which never recorded
-- most outcomes (266 proposals since 2020 were never marked won or lost). Ian: "those proposal win rates are basically useless".
DELETE FROM opportunities.PlanPoint WHERE Id = 6;
GO

MERGE opportunities.PlanRhythm AS t
USING (VALUES
    (1, N'Ian''s daily planning hour', N'Ian, every weekday morning, in the app.', N'The day''s sales, project and IT priorities, with the emails already drafted.', N'All of them: it is where the plan becomes today''s work.', 1, 9, 10),
    (2, N'Weekly to-do list for each lead', N'Each lead, from Ian, every week; sooner if something is urgent.', N'A short numbered list: who to call, what to send, by when. One line back per item: done, new date, or dropped and why.', N'Every opportunity has a next step; no proposal waits past 60 days for an answer.', 2, 5, 20),
    (3, N'Monday action list', N'Ian to each lead, with Monday''s email.', N'Anything they agreed to do at a meeting that is still open.', N'Actions closed faster than they open ({ActionsOpen} open today).', 2, 5, 30),
    (4, N'Check-in with each regional lead', N'Ian with Omar, Islam, Conor and Rory; 15 minutes every two weeks.', N'Their replies, their top three opportunities, and one thing they need from Ian.', N'A fee estimate on every opportunity; an answer on every proposal.', 1, 5, 40),
    (5, N'Check-in with JM and Jim', N'Ian with JM and Jim; 20 minutes every two weeks, from the week of 26 Oct.', N'Their opportunities, then projects going over budget and invoices not yet paid.', N'Invoicing above $400k a month; over 90 days under $700k.', 2, 8, 50),
    (6, N'Sales meeting, monthly', N'The regional leads and JM; under half an hour; first Fri 13 Nov, 2:00.', N'What was won or lost, what is overdue and with whom, and one topic (first: getting an answer on every proposal).', N'An answer on every proposal, with who won and why.', 2, 5, 60),
    (7, N'Operations meeting, monthly', N'JM and Jim; under half an hour; first Thu 29 Oct, 9:00.', N'Money owed to KOR, projects losing hours, timesheets, and three decisions.', N'Days to collect down to 120; inactive money decided; over-budget down.', 6, 8, 70),
    (8, N'Quarterly review', N'Ian and the partners; first planned for January 2027.', N'Is the plan working: goals hit or missed, where to focus, what to change.', N'Keeps the plan true.', 1, 9, 80)
) AS s (Id, Name, WhoWhen, WhatHappens, Moves, StageFrom, StageTo, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET Name = s.Name, WhoWhen = s.WhoWhen, WhatHappens = s.WhatHappens, Moves = s.Moves,
    StageFrom = s.StageFrom, StageTo = s.StageTo, SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Name, WhoWhen, WhatHappens, Moves, StageFrom, StageTo, SortOrder)
    VALUES (s.Id, s.Name, s.WhoWhen, s.WhatHappens, s.Moves, s.StageFrom, s.StageTo, s.SortOrder);
GO

MERGE opportunities.PlanPillar AS t
USING (VALUES
    (N'BD', N'Business development apps', N'A sales database, market research, a bid team, a review of every win and loss.',
        N'The BD Director (Ian''s daily planning hour, drafted emails, meetings); new-project alerts from {SourcesLive} sources; the opportunity list showing where every bid stands, with a weekly to-do list for each lead; bid documents saved automatically; a file on each client; a library built from BD books and KOR''s own documents.',
        N'{Signals30d} new opportunities found in 30 days. Since 2020, 266 proposals were never marked won or lost, so KOR''s real win rate is not yet known.',
        N'Every proposal closed out, won or lost, with who won and why; a fee estimate on every live opportunity.', 10),
    (N'Fin', N'Financials', N'A finance office: daily cash, receivables, backlog, project margins, a monthly close.',
        N'Financials, built in-house on Deltek: one agreed definition for every number; the Executive Summary (cash, money owed by age, days to collect, backlog, billings, net multiplier, labour margin, net income); profit after overhead on each of the {ActiveProjects} active projects; partner billing history; staff utilization and labour cost; profit and loss; a Collections screen with a case for each overdue account; and the key numbers recorded every morning since 18 October.',
        N'Net income {Exec.NetIncomeT12mo} over 12 months, net multiplier {Exec.NetMultiplier}. {LosingDoneProjectsLoss} lost after overhead on {LosingDoneProjects} projects that are 90%+ billed. {ArInactive} owed on projects no longer active. {InvoicedMonthAvg6} a month invoiced over six months, against {InvoicedMonthAvgPrior6} before.',
        N'A partner assigned to chase overdue invoices, working from the Collections screen.', 20),
    (N'Ops', N'Operations', N'Operations reviews, resource planning, project controls, a standard way of working.',
        N'The operations section of Ian''s daily planning hour; project managers'' workload and missing timesheets; lists of projects over budget and billing slowly; project tools for the engineers (email filing, transmittals, standard details, drawing-to-model tools).',
        N'{OverBudgetProjects} of {ActiveProjects} active projects over their hours budget ({OverBudgetHours} past budget), {CriticalProjects} critical, {BillingLagProjects} billing slower than they burn hours.',
        N'Extra-service letters tracked as a step; the monthly Operations meeting from 29 October.', 30),
    (N'IT', N'IT', N'Monitoring on every device, patching on schedule, a service desk, backups that are tested.',
        N'NetworkOps, KOR''s own IT monitoring: the health of every PC and server, the server rack, battery backup and firewall, updates, fixes done remotely, and the IT items in Ian''s daily planning hour.',
        N'{ItCriticalGroups} critical issue groups open today, {ItWarningGroups} warnings; {ItUpdatesDue} machines with security updates due.',
        N'Patch what is due; the backup path and server reboots already planned; retire any paid tool the platform now replaces. Because of NetworkOps, outside IT spend comes down to antivirus, Veeam, Office 365, connectivity and phones.', 40)
) AS s (PillarKey, Title, Fortune500, KorHas, Found, NextText, SortOrder)
ON t.PillarKey = s.PillarKey
WHEN MATCHED THEN UPDATE SET Title = s.Title, Fortune500 = s.Fortune500, KorHas = s.KorHas, Found = s.Found,
    NextText = s.NextText, SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (PillarKey, Title, Fortune500, KorHas, Found, NextText, SortOrder)
    VALUES (s.PillarKey, s.Title, s.Fortune500, s.KorHas, s.Found, s.NextText, s.SortOrder);
GO
