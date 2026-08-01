/*
  292_RetirePerishableIntelActions.sql  (2026-07-18, mess audit R4 — revised)

  The June 2026 research era created 21,853 Open IntelActions. Most are a
  durable reference library (ContactStrategy / PursuitAngle / HowToGetOnRoster
  / WarmIntroPath / triage notes) consumed by the app's org dossiers, brief
  generators, and /ask — those STAY (Ian's check: "won't kill real
  opportunities we just haven't actioned?" — correct instinct; blanket
  retirement was rejected).

  What retires here: the PERISHABLE, time-anchored classes only — a June
  "TimingWindow: act this month" surfacing in a fresh brief is stale advice
  presented as current (the Emeryville failure class). All >30 days old at
  retirement. Archive-not-delete; reversible.
*/

SET QUOTED_IDENTIFIER ON;

UPDATE opportunities.IntelAction SET
    RetiredAtUtc  = sysdatetimeoffset(),
    RetiredReason = N'Perishable time-anchored class from the June 2026 research era; stale as guidance by 2026-07-18 (mess-audit R4). Durable reference classes retained.'
WHERE RetiredAtUtc IS NULL
  AND Status = N'Open'
  AND CreatedAtUtc < DATEADD(DAY, -30, sysdatetimeoffset())
  AND ActionType IN (N'TimingWindow', N'Monitor', N'MonitorPhase', N'MonitorFlag',
                     N'MonitorSignal', N'watch', N'EngagementTiming');

SELECT RetiredNow = @@ROWCOUNT,
       StillOpen  = (SELECT COUNT(*) FROM opportunities.IntelAction WHERE RetiredAtUtc IS NULL AND Status = N'Open');
