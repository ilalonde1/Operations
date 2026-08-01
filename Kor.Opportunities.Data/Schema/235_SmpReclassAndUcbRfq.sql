USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 235: two dispositions from the overnight enrichment review.
  1) SMP Engineering (15548) - reclassify Competitor -> Vendor. It is a Calgary
     MEP/electrical consultancy (already enrichment-suppressed via m132), not a
     structural competitor; potential allied teaming partner (taxonomy TBD).
  2) Stand up the live pursuit the public-sector agent surfaced: UC Berkeley
     RSSP Emeryville Structural Repairs RFQ - prequalification due 2026-07-15.
     A direct structural-engineering solicitation (open SE seat) under UC
     Berkeley (org 76921).
*/
BEGIN TRAN;

-- 1) SMP reclassify
UPDATE opportunities.CanonicalOrg
   SET Kind = N'Vendor',
       Notes = COALESCE(Notes, N'MEP/electrical allied-discipline consultancy (Calgary; Vancouver office). Potential teaming partner on KOR structural teams - NOT a structural competitor. Reclassified from Competitor 2026-06-20; enrichment suppressed via m132. Teaming-partner taxonomy TBD.'),
       UpdatedAtUtc = sysdatetimeoffset()
 WHERE Id = 15548;

-- 2) UC Berkeley RSSP Emeryville Structural Repairs RFQ (open SE seat)
IF NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory WHERE SourceKey = N'overnight-enrich-2026-06-20:ucb-rssp-emeryville')
INSERT INTO opportunities.MajorProjectsInventory
  (Province, SourceKey, ProjectName, ProponentName, ProponentCanonicalOrgId, MunicipalityName,
   Stage, SeatStatus, ProjectDescription, ScheduleNotes, KorPipelineTag,
   FirstSeenAtUtc, LastSeenAtUtc, UpdatedAtUtc)
VALUES
  (N'CA', N'overnight-enrich-2026-06-20:ucb-rssp-emeryville',
   N'UC Berkeley RSSP - Emeryville Structural Repairs (RFQ)',
   N'University of California, Berkeley', 76921, N'Emeryville',
   N'RFQ - prequalification (due 2026-07-15)', N'Open',
   N'Direct structural-engineering solicitation via UC Berkeley Real Estate / RSSP for structural repairs at the Emeryville (Aquatic Park / Emery Station vicinity) holdings. Open SE seat KOR can pursue.',
   N'PREQUALIFICATION DUE 2026-07-15. Surfaced by overnight enrichment (public-sector agent). UCB contacts: Wendy Hillis (Campus Architect), Todd Henry. Verify exact RFQ scope + submission portal before the deadline.',
   N'active-pursuit',
   sysdatetimeoffset(), sysdatetimeoffset(), sysdatetimeoffset());

PRINT 'Migration 235: SMP reclassified to Vendor; UC Berkeley Emeryville RFQ tracked.';
COMMIT TRAN;
GO
