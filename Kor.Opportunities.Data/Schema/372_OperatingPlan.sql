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
            CHECK (Section IN (N'WhereToPlay', N'HowWeWin', N'WhereItHurts', N'Priorities', N'Decide')),
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
    (1, N'BD', N'Signal', N'The radar finds; the regional lead picks.',
        N'Permits, rezonings, tenders and news about the people KOR works with.',
        N'{Signals30d} new in 30 days, from {SourcesLive} of {SourcesTotal} sources. Only you see them so far.',
        N'Nightly ingestion, the relevance gate, client dossiers in the Director.',
        N'Next step: release the app to the leads (the pilot for Omar, Islam and Jim, promised on 9 October), so each lead sees the signals in their region and makes the good ones pursuits.',
        N'Watch'),
    (2, N'BD', N'Pursuit', N'Regional lead: Omar Vancouver, Islam Alberta, Conor Okanagan, Rory Island, Jim US; JM.',
        N'A named project or client with an owner and a next step.',
        N'{PursuitsLive} live; {PursuitsWithFee} carry a fee; {PursuitsUntouched14} untouched for 14 days or more.',
        N'The CRM, and a weekly roll-up per owner: first touch by day 7, keep warm every 21 days, next step when due.',
        N'Calls and meetings are rarely logged, so most roll-up asks read "first touch".',
        N'Broken'),
    (3, N'BD', N'Proposal', N'The lead, with the engineer who will run the job.',
        N'Go or no-go, then the fee proposal (filed in Project Opportunities\Pending).',
        N'{PursuitsTender} live tender pursuits, so {PursuitsTender} go/no-go decisions on record.',
        N'For tenders: go/no-go 2 days after it opens, draft review a week before the deadline, submission check 2 days before.',
        N'Questions deadlines and addenda are not captured, so the system cannot warn on them.',
        N'Watch'),
    (4, N'BD', N'Decision', N'The lead.',
        N'Chasing the client until they decide.',
        N'CRM: {CrmWonCount} won, {CrmLostCount} lost; {SubmittedOver60} submitted over 60 days with no outcome. Since 2020: 43% of sent proposals won, 266 never closed.',
        N'Follow-up at 14 days, chase the decision at 45, both in the owner''s roll-up.',
        N'The win/loss review found no standing close-out step before October 2026.',
        N'Broken'),
    (5, N'BD', N'Won or lost', N'The lead, handing to the project manager.',
        N'Won: hand the job to delivery. Lost: who won, and why.',
        N'Of 291 lost or unanswered proposals since 2020 with a comment, fewer than 30 say why.',
        N'Hand-off and loss-reason asks within 30 days, in the roll-up.',
        N'Loss reasons are what "how KOR wins" should learn from, and they are mostly missing.',
        N'Watch'),
    (6, N'Ops', N'Delivery', N'The project manager; JM and Jim in the COO seat.',
        N'Designing the job inside its fee.',
        N'{OverBudgetProjects} of {ActiveProjects} active projects over their hours budget, {OverBudgetHours} past budget in all; {CriticalProjects} critical; {LosingProjects} behind on billing or losing after overhead ({LosingProjectsLoss}), of which {LosingDoneProjects} are 90%+ billed and lost {LosingDoneProjectsLoss}; utilization {Exec.Utilization}.',
        N'The Financials engine, the COO monthly, the COO moves in your morning hour.',
        N'Proposed: an extra-service letter before a project passes its hours budget, tracked as a step, not found afterwards.',
        N'Broken'),
    (7, N'Ops', N'Invoice', N'The project manager.',
        N'Billing what has been earned, on time.',
        N'{InvoicedMonthAvg6} a month over the last six months, against {InvoicedMonthAvgPrior6} the six before; {BillingLagProjects} projects bill slower than they burn hours.',
        N'The billing-lag list from the Financials engine, in the morning brief.',
        N'Proposed: the billing-lag list is reviewed at each PM''s quickie, not only read by you.',
        N'Broken'),
    (8, N'Ops', N'Cash', N'Proposed: a named partner for every invoice over 90 days.',
        N'Collecting what has been billed.',
        N'{Exec.DaysSalesOutstanding} days to collect; cash {Exec.CashPosition}; {ArTotal} owed; {ArOver90} over 90 days; {ArInactive} on projects no longer active.',
        N'The scoreboard records it every morning; the COO monthly pre-read lists the largest.',
        N'The collections module is barely built, so chasing is by hand.',
        N'Broken'),
    (9, N'IT', N'NetworkOps', N'You.',
        N'Every PC and server healthy, patched and backed up, underneath every stage.',
        N'{ItCriticalGroups} critical issue groups, {ItWarningGroups} warnings; {ItUpdatesDue} machines with security updates due.',
        N'NetworkOps: health of every PC and server, rack, UPS and firewall monitoring, updates, remote fixes.',
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
    (1, N'BD', 2, N'Every live pursuit has a fee estimate', N'PursuitsWithFee', 0.0, NULL, N'every live pursuit', 10),
    (2, N'BD', 2, N'No live pursuit untouched for 14 days or more', N'PursuitsUntouched14', 61.0, 0.0, N'none', 20),
    (3, N'BD', 4, N'No proposal sits submitted past 60 days without an outcome', N'SubmittedOver60', NULL, 0.0, N'none', 30),
    (4, N'BD', 4, N'Submitted-to-won from 43% toward 50%', NULL, 43.0, 50.0, N'50% of proposals sent', 40),
    (5, N'BD', 1, N'The app in the leads'' hands', NULL, NULL, NULL, N'pilot: Omar, Islam and Jim, with a one-page how-to', 50),
    (6, N'Ops', 7, N'Invoicing back above $400k a month', N'InvoicedMonthAvg6', 367000.0, 400000.0, N'$400k a month', 60),
    (7, N'Ops', 8, N'Days to collect down to 120', N'Exec.DaysSalesOutstanding', NULL, 120.0, N'120 days', 70),
    (8, N'Ops', 8, N'Owed over 90 days under $700k', N'ArOver90', NULL, 700000.0, N'under $700k', 80),
    (9, N'Ops', 8, N'The money on inactive projects decided', N'ArInactive', NULL, NULL, N'each collected, settled or written off, with the reason', 90),
    (10, N'Ops', 6, N'Over-budget projects down, the worst ten with change orders', N'OverBudgetProjects', NULL, NULL, N'down from the baseline', 100),
    (11, N'IT', 9, N'Every machine on this month''s security updates', N'ItUpdatesDue', NULL, 0.0, N'none due', 110),
    (12, N'IT', 9, N'No critical IT issue left open', N'ItCriticalGroups', NULL, 0.0, N'none', 120),
    (13, N'IT', 9, N'Outside IT spend: antivirus, Veeam, Office 365, connectivity and phones only', NULL, NULL, NULL, N'nothing else', 130),
    (14, N'Ops', 6, N'Money given away on finished projects down, the worst recovered by change orders', N'LosingDoneProjectsLoss', NULL, NULL, N'down from the baseline', 95)
) AS s (Id, Pillar, StageNo, Title, Metric, BaselineValue, TargetValue, TargetText, SortOrder)
ON t.Id = s.Id
WHEN MATCHED AND t.Status = N'Recommended' THEN UPDATE SET Pillar = s.Pillar, StageNo = s.StageNo, Title = s.Title,
    Metric = s.Metric, BaselineValue = s.BaselineValue, TargetValue = s.TargetValue, TargetText = s.TargetText,
    SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Pillar, StageNo, Title, Metric, BaselineValue, TargetValue, TargetText, DueDate, SortOrder)
    VALUES (s.Id, s.Pillar, s.StageNo, s.Title, s.Metric, s.BaselineValue, s.TargetValue, s.TargetText, '2027-03-31', s.SortOrder);
GO

MERGE opportunities.PlanPoint AS t
USING (VALUES
    (1, N'BD', N'WhereToPlay', N'Grow the developers who already choose KOR.', N'Westland, Northland, Cressey, Domus, Kerkhoff and Polygon: 40 won, 1 lost since 2020. Each gets a named partner and a standing check-in, not a proposal-by-proposal relationship.', 10),
    (2, N'BD', N'WhereToPlay', N'Metro Vancouver and South of Fraser are the base.', N'220 of 281 wins since 2020; South of Fraser converts 81% of decided proposals.', 20),
    (3, N'BD', N'WhereToPlay', N'The US is the growth market.', N'22 wins at 65% decided since 2020, and Jim''s live US pursuits.', 30),
    (4, N'BD', N'WhereToPlay', N'Qualify hard before writing a fee for the clients who rarely convert.', N'NSDA, Wesgroup, Strand, Starlight and Porte: 12 won, 15 lost, 35 never decided. A go/no-go before the proposal, not after.', 40),
    (5, N'BD', N'HowWeWin', N'Get in as the architect''s engineer before the RFP.', N'The structural engineer is usually chosen at the start of schematic design.', 50),
    (6, N'BD', N'HowWeWin', N'Mid-size fees convert best.', N'$25-100k wins 80% of decided proposals; $250k and up wins 58%. Big bids are deliberate bets.', 60),
    (7, N'BD', N'HowWeWin', N'Close every proposal.', N'Follow up at 14 days, chase the decision at 45, and record who won and why. Glotman Simpson is the competitor named most often (6 of the stated losses).', 70),
    (8, N'BD', N'HowWeWin', N'Spread the selling.', N'One principal leads 345 of 701 pursuits since 2020 (49%). Six owners now hold the {PursuitsLive} live pursuits.', 80),
    (22, N'Ops', N'WhereItHurts', N'The bottom line.', N'Net income {Exec.NetIncomeT12mo} over 12 months on a labour margin of {Exec.LaborMarginT12mo}; net multiplier {Exec.NetMultiplier}. {LosingDoneProjects} projects that are 90%+ billed lost {LosingDoneProjectsLoss} after overhead: money given away. By fee type: {LosingDoneFixedFeeLoss} on {LosingDoneFixedFee} fixed-fee jobs (fee or scope), {LosingDoneMixedLoss} on {LosingDoneMixed} fixed-fee jobs with hourly extras, {LosingDoneHourlyLoss} on {LosingDoneHourly} hourly jobs (rates or write-offs). {LosingProjects} more read as losing on billing to date ({LosingProjectsLoss}), many only because billing lags the hours; {ProjectsBelowMultiplier2} are below a 2.0 multiplier.', 85),
    (9, N'Ops', N'WhereItHurts', N'Cash.', N'{ArTotal} owed, {ArOver90} of it over 90 days, and {ArInactive} on projects no longer on the active list.', 90),
    (10, N'Ops', N'WhereItHurts', N'Overruns.', N'{OverBudgetProjects} of {ActiveProjects} active projects are over their hours budget, {OverBudgetHours} past budget in all, given away unless charged; {CriticalProjects} are critical; {BillingLagProjects} are burning hours faster than they bill.', 100),
    (11, N'Ops', N'WhereItHurts', N'Capacity.', N'Utilization is {Exec.Utilization} over the last 30 days.', 110),
    (12, N'Ops', N'Priorities', N'Collect.', N'A partner owns every invoice over 90 days and reports at the quickie. Decide the money on inactive projects: collect, settle or write off.', 120),
    (13, N'Ops', N'Priorities', N'Charge for changes before the hours are spent.', N'Extra-service letters on the worst ten over-budget projects first.', 130),
    (14, N'Ops', N'Priorities', N'Bill on time.', N'Close the gap on the projects where billing lags the hours.', 140),
    (15, N'Ops', N'Priorities', N'Fix the process before January.', N'The rush should land on a working cadence, not a backlog.', 150),
    (16, N'IT', N'Priorities', N'Patch on schedule.', N'Every machine on the month''s security updates; {ItUpdatesDue} have updates due today.', 160),
    (17, N'IT', N'Priorities', N'Own the tools, keep the cost line.', N'Because of NetworkOps, outside IT spend comes down to antivirus, Veeam backups, Office 365, connectivity and phones.', 170),
    (18, N'All', N'Decide', N'The goals and their numbers.', N'The figures are starting points from KOR''s record, not commitments, until you decide them.', 180),
    (19, N'All', N'Decide', N'Which repeat developers each partner owns,', N'and who qualifies the low-converting clients.', 190),
    (20, N'All', N'Decide', N'The money on inactive projects.', N'{ArInactive} today; 31103-01 alone is $502k, last invoiced February 2024.', 200),
    (21, N'All', N'Decide', N'Whether the US gets more than Jim''s time.', N'One person sells the US.', 210)
) AS s (Id, Pillar, Section, Title, Detail, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET Pillar = s.Pillar, Section = s.Section, Title = s.Title, Detail = s.Detail, SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Pillar, Section, Title, Detail, SortOrder)
    VALUES (s.Id, s.Pillar, s.Section, s.Title, s.Detail, s.SortOrder);
GO

MERGE opportunities.PlanRhythm AS t
USING (VALUES
    (1, N'Daily hour', N'You. Email at 6:05 on weekdays; the same hour in the Director.', N'Ranked BD, COO and IT moves, drafts ready to send, the roll-ups due to forward.', N'All of them: it is where the plan becomes today''s work.', 1, 9, 10),
    (2, N'Pursuit roll-ups', N'You forward; each pursuit owner replies. Every 7 days, sooner when something is urgent.', N'Numbered asks (Do, By, Why). One line back per item: done, moved, or dropped and why.', N'Every pursuit has a next step; no proposal past 60 days without an outcome.', 2, 5, 20),
    (3, N'Monday lists', N'You, to each lead, with Monday''s email.', N'Each lead''s open actions from the meetings, as drafts.', N'Actions closed faster than they open ({ActionsOpen} open today).', 2, 5, 30),
    (4, N'Quickie, regional lead', N'You with Omar, Islam, Conor, Rory. 15 min every two weeks; first is Omar, Tue 20 Oct, 10:00.', N'Their roll-up replies, their top three pursuits, one ask.', N'A fee estimate on every pursuit; submitted-to-won toward 50%.', 1, 5, 40),
    (5, N'Quickie, BD + COO', N'You with JM and Jim. 20 min every two weeks, from the week of 26 Oct.', N'Their pursuits, then their projects over budget and invoices over 90 days.', N'Invoicing above $400k a month; over 90 days under $700k.', 2, 8, 50),
    (6, N'BD monthly', N'Leads and JM. 27 min; first Fri 13 Nov, 2:00.', N'What closed, what is late by owner, one topic (first: closing out every fee).', N'Close every proposal; submitted-to-won.', 2, 5, 60),
    (7, N'COO monthly', N'JM and Jim. 27 min; first Thu 29 Oct, 9:00.', N'Cash and collections, projects losing hours, timesheets, three decisions.', N'Days to collect down to 120; inactive money decided; over-budget down.', 6, 8, 70),
    (8, N'Quarterly review', N'You and the partners. Proposed: first in January 2027.', N'The plan against the scoreboard: goals hit or missed, where to play, what to change.', N'Keeps the plan true.', 1, 9, 80)
) AS s (Id, Name, WhoWhen, WhatHappens, Moves, StageFrom, StageTo, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET Name = s.Name, WhoWhen = s.WhoWhen, WhatHappens = s.WhatHappens, Moves = s.Moves,
    StageFrom = s.StageFrom, StageTo = s.StageTo, SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Name, WhoWhen, WhatHappens, Moves, StageFrom, StageTo, SortOrder)
    VALUES (s.Id, s.Name, s.WhoWhen, s.WhatHappens, s.Moves, s.StageFrom, s.StageTo, s.SortOrder);
GO

MERGE opportunities.PlanPillar AS t
USING (VALUES
    (N'BD', N'Business development apps', N'A CRM, market intelligence, a bid desk, win/loss review.',
        N'The BD Director (your daily hour, drafts, meetings); the early-signal radar on {SourcesLive} sources; the CRM with stages and weekly owner roll-ups; client dossiers; a knowledge library built from BD books and KOR''s own documents.',
        N'{Signals30d} new signals in 30 days. KOR''s real win/loss record, rebuilt from 4,785 past pursuits: 72% when a client decides, 43% of everything sent.',
        N'The app in the leads'' hands (pilot: Omar, Islam, Jim); open fees into the CRM; tender questions and addenda.', 10),
    (N'Fin', N'Financials', N'A finance office: daily cash, receivables, backlog, project margins, a monthly close.',
        N'The Financials module, built in-house on Deltek: a defined metric dictionary; the Executive Summary (cash, liquidity, AR aging, DSO, backlog, billings, net multiplier, labour margin, net income); per-project multiplier and net profit after overhead across all {ActiveProjects} active projects; partner billing history; staff utilization and labour cost; a GL profit and loss; and the impact scoreboard, recorded every morning since 18 October.',
        N'Net income {Exec.NetIncomeT12mo} over 12 months, net multiplier {Exec.NetMultiplier}. {LosingDoneProjectsLoss} lost after overhead on {LosingDoneProjects} projects that are 90%+ billed. {ArInactive} owed on projects no longer active, $502k of it on one job last invoiced in February 2024. {InvoicedMonthAvg6} a month invoiced over six months, against {InvoicedMonthAvgPrior6} before.',
        N'Collections: the module is barely built, so chasing is by hand. The monthly scoreboard page.', 20),
    (N'Ops', N'Operations', N'Operations reviews, resource planning, project controls, a standard way of working.',
        N'The COO brief in your daily hour; PM workload and timesheet completeness; over-budget and billing-lag lists; project tools for the engineers (email filing, transmittals, standard details, drawing-to-model tools).',
        N'{OverBudgetProjects} of {ActiveProjects} active projects over their hours budget ({OverBudgetHours} past budget), {CriticalProjects} critical, {BillingLagProjects} billing slower than they burn hours.',
        N'Extra-service letters tracked as a step; the COO monthly from 29 October.', 30),
    (N'IT', N'IT', N'Monitoring on every device, patching on schedule, a service desk, backups that are tested.',
        N'NetworkOps, KOR''s own fleet control: health of every PC and server, rack, UPS and firewall monitoring, updates, remote fixes, and the IT moves in your daily hour.',
        N'{ItCriticalGroups} critical issue groups open today, {ItWarningGroups} warnings; {ItUpdatesDue} machines with security updates due.',
        N'Patch what is due; the backup path and server reboots already planned; retire any paid tool the platform now replaces. Because of NetworkOps, outside IT spend comes down to antivirus, Veeam, Office 365, connectivity and phones.', 40)
) AS s (PillarKey, Title, Fortune500, KorHas, Found, NextText, SortOrder)
ON t.PillarKey = s.PillarKey
WHEN MATCHED THEN UPDATE SET Title = s.Title, Fortune500 = s.Fortune500, KorHas = s.KorHas, Found = s.Found,
    NextText = s.NextText, SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (PillarKey, Title, Fortune500, KorHas, Found, NextText, SortOrder)
    VALUES (s.PillarKey, s.Title, s.Fortune500, s.KorHas, s.Found, s.NextText, s.SortOrder);
GO
