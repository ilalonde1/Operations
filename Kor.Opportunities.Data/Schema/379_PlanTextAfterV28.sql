-- 379: the operating plan's text after V28 (2026-10-21). Three things the plan said were still to come are done:
-- Alberta Purchasing Connection documents download on a grab (migration 376, signed-in portal sessions), the app is on
-- every regional lead's PC (fleet V28), and Claude's RFP review prepares the proposal checklist when a tender is
-- grabbed (migrations 377-378). 372 seeds the same rows and carries the same new text, so re-running it changes nothing.
-- Each update matches the old wording exactly, so a row already edited by hand is left alone. Idempotent.
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

UPDATE opportunities.PlanPillar
   SET NextText = N'Every proposal closed out, won or lost, with who won and why; a fee estimate on every live opportunity.'
 WHERE PillarKey = N'BD'
   AND NextText = N'The app on every regional lead''s computer; Alberta Purchasing Connection bid documents once the company sign-in is confirmed; every proposal closed out, won or lost.';

UPDATE opportunities.PlanPoint
   SET Detail = N'Live. The bid package of every Bids&Tenders and Alberta Purchasing Connection tender we pursue is saved with the opportunity minutes after it is grabbed, and checked daily for addenda until it closes.'
 WHERE Detail = N'Live. The bid package of every Bids&Tenders tender we pursue is saved with the opportunity automatically, and checked daily for addenda until it closes; Alberta Purchasing Connection documents join as soon as the company sign-in is confirmed.';

UPDATE opportunities.PlanPoint
   SET Detail = N'Live. Claude reads the RFP of every tender we pursue: bid or not and why, the questions for the buyer, and a checklist of everything the client asked for; with the rate sheet and the team chart. First used on Leduc.'
 WHERE Detail = N'Live. A checklist of everything the client asked for, the rate sheet and the team chart, prepared for them; first used on Leduc.';

UPDATE opportunities.PlanStage
   SET WeakPoint = N'Claude''s RFP review runs from Ian''s PC, so a tender grabbed while he is away can wait a day for it; the bid documents are there at once.'
 WHERE WeakPoint = N'The proposal checklist is prepared from each RFP when the lead asks for it; Leduc was the first.';

SELECT (SELECT COUNT(*) FROM opportunities.PlanPillar WHERE NextText LIKE N'Every proposal closed out%') AS PillarNew,
       (SELECT COUNT(*) FROM opportunities.PlanPoint WHERE Detail LIKE N'Live. The bid package of every Bids&Tenders and Alberta%') AS DocsNew,
       (SELECT COUNT(*) FROM opportunities.PlanPoint WHERE Detail LIKE N'Live. Claude reads the RFP%') AS ChecklistNew,
       (SELECT COUNT(*) FROM opportunities.PlanStage WHERE WeakPoint LIKE N'Claude''s RFP review runs%') AS WeakNew;

COMMIT TRANSACTION;
