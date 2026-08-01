/*
193_CleanCaSeatsRehone.sql
WHAT: Encode the 2026-06-18 re-hone verification corrections into the CA seat rows so the DB
      stops implying refuted "open" seats.
WHY:  Re-hone (verify-ecosystem-claims) found the ecosystem research conflated "SE unnamed in
      press" with "open seat" — 6/6 refuted. Confirmed: OC Vibe SE = JAMA; KOR = SEoR on
      800 Broadway; the rest are locked/under-construction. Clean the data to match.
HOW:  Targeted UPDATEs by MPI Id (verified 2026-06-18). JAMA survivor=68613, KOR=38918.
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();

-- 800 Broadway (8289): KOR is the SEoR — link KOR's canonical org (a confirmed CA credential)
UPDATE opportunities.MajorProjectsInventory
  SET StructuralEngineerName = N'KOR Structural Ltd.', StructuralEngineerCanonicalOrgId = 38918,
      ProjectDescription = COALESCE(ProjectDescription + N' ', N'') + N'[re-hone 2026-06-18: KOR = SEoR, verified; CA Ventures/JWDA, delivered 2025 — a KOR credential.]',
      UpdatedAtUtc = @now
  WHERE Id = 8289 AND Province = N'CA';

-- OC Vibe (8280): NOT open — John A. Martin (JAMA) is the named incumbent SE
UPDATE opportunities.MajorProjectsInventory
  SET StructuralEngineerName = N'John A. Martin & Associates', StructuralEngineerCanonicalOrgId = 68613,
      ProjectDescription = COALESCE(ProjectDescription + N' ', N'') + N'[re-hone 2026-06-18: SE = John A. Martin (JAMA) — the earlier "open seat" claim was REFUTED.]',
      UpdatedAtUtc = @now
  WHERE Id = 8280 AND Province = N'CA';

-- Stevens Creek (8282) + Mission College (8279): stage-gate says SE likely already engaged — temper from "open"
UPDATE opportunities.MajorProjectsInventory
  SET ProjectDescription = COALESCE(ProjectDescription + N' ', N'') + N'[re-hone 2026-06-18: permit-ready/approved → SE likely already engaged (stage-gate). Treat as LIKELY-LOCKED, not confirmed-open; verify only via direct John Wayland / Irvine Co. outreach.]',
      UpdatedAtUtc = @now
  WHERE Id IN (8282, 8279) AND Province = N'CA';

-- Bolsa Pacific (8275) + Coyote Creek (8335): under construction → SE engaged (unnamed); not open
UPDATE opportunities.MajorProjectsInventory
  SET ProjectDescription = COALESCE(ProjectDescription + N' ', N'') + N'[re-hone 2026-06-18: under construction → SE engaged (unnamed publicly); NOT an open seat.]',
      UpdatedAtUtc = @now
  WHERE Id IN (8275, 8335) AND Province = N'CA';

COMMIT;

SELECT Id, LEFT(ProjectName,32) AS Proj, Stage, StructuralEngineerName AS SE, StructuralEngineerCanonicalOrgId AS SeId
FROM opportunities.MajorProjectsInventory
WHERE Id IN (8289, 8280, 8282, 8279, 8275, 8335) ORDER BY Id;
