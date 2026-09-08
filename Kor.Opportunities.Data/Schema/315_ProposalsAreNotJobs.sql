-- Migration 315 (2026-09-08): record that a Deltek project record is NOT a won job,
-- and correct the three claims Rory Beirne caught.
--
-- ⛔ THE CLASS. `CanonicalOrg.KorProjectsCount` counts Deltek PROJECT RECORDS. A
--    record is created when we decide to chase something, so the count includes
--    live pursuits, lost bids and jobs never invoiced. Measured 2026-09-08 across
--    all 10,017 top-level projects: only **2,411 have ever been billed**.
--
--    Deltek already knows the difference and we were reading the wrong column:
--      PR.Stage   InPursuit 179 · LOST 86 · DNP 8 · ~WDEF~ 9,345 · null(+P) 350
--      PR.ChargeType  P = promotional 350; R = regular 9,345
--    `ChargeType = 'R'` admits 7,255 never-billed records. `Stage` does not.
--
--    Two accessors in this solution disagree, which is how it survived:
--      Kor.Operations.Data/Deltek/DeltekKorWonProjectAccessor.cs -> ChargeType='R'  (wrong)
--      Kor.Operations.App/Crm/DeltekClientContextService.cs      -> joins AR       (right)
--    Neither uses Stage as the primary test. See
--    docs/island-pipeline/NOTE-architect-role-and-won-definition-2026-09-08.md.
--
-- What this cost, in Rory's words: "I think we are counting proposals assuming
-- they are jobs." He is right, and it put three false claims in front of him.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

IF COL_LENGTH('opportunities.CanonicalOrg', 'KorProjectsCountIsRecordsNotWins') IS NULL
    ALTER TABLE opportunities.CanonicalOrg
        ADD KorProjectsCountIsRecordsNotWins bit NOT NULL
            CONSTRAINT DF_CanonicalOrg_KorCountCaveat DEFAULT (1);
GO

-- The corrections, as typed facts so the next pass starts from them.
IF OBJECT_ID('tempdb..#f') IS NOT NULL DROP TABLE #f;
CREATE TABLE #f (OrgId bigint, FactType nvarchar(60), Body nvarchar(max), SourceUrl nvarchar(400));

INSERT INTO #f (OrgId, FactType, Body, SourceUrl) VALUES
(68998, N'CompetitorNote',
 N'CORRECTION 2026-09-08, Rory Beirne: KOR did NOT win Westmark''s Gracewood at Fairwinds, Nanoose. Deltek project 20330-01 is Stage = InPursuit with zero contract fee and zero billed - Deltek knew, and an earlier read of ChargeType instead of Stage turned a live pursuit into a claimed win. Herold remains the mid-Island incumbent and we have NOT taken a job off them. Herold was acquired by Englobe in May 2025; 70+ staff, Nanaimo HQ plus Victoria and Ucluelet.',
 NULL),
(927808, N'CompetitorNote',
 N'CORRECTION 2026-09-08, Rory Beirne: Sense Engineering is HEADQUARTERED IN NORTH VANCOUVER, not Victoria. Their own site lists the Victoria address first (631 Granrose Terrace, a residential-style street in the Colwood/Langford V9C area) which is what an earlier read took for a head office; the North Vancouver office at 104-788 Copping St is a commercial suite. They have 12 offices. The rest of the earlier note stands: not a direct competitor today - envelope, restoration, inspection, capital planning, lots of small jobs, working the relationships on jobs KOR is already on - but hiring a Structural Engineering Group Lead ($150-175k) to expand into new construction with BC work focused on mass timber and hybrid structures.',
 N'https://senseengineering.com/'),
(13, N'RiskNote',
 N'⛔ NOT A BILLING CLIENT. D Akers Property Solutions has NINE Deltek project records and ZERO ever billed - six in Campbell River, all Stage = InPursuit or never invoiced. An earlier dossier called this "our deepest mid-Island relationship". It is our most-pursued mid-Island prospect. Rory Beirne 2026-09-08: "We bid $5K to Akers for sites in Lantzville and didn''t get the job." Single-family fees are too low to make money on and are hard to win.',
 NULL),
(80, N'RiskNote',
 N'⛔ COUNT CORRECTION 2026-09-08. Starlight has TWELVE Deltek project records and ZERO ever billed. Two carry a signed contract fee - 31180-01 7376 Halifax St Burnaby at $145,000 and 31229-01 Quadra East Victoria at $180,000 - and labour has been charged against the Burnaby one, so those are real wins in progress, not completed jobs. The other ten are pursuits. Earlier dossiers said "6 projects". The identity correction below still stands.',
 NULL);

INSERT INTO opportunities.OrgFact
    (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
SELECT CONVERT(varchar(40), HASHBYTES('SHA1',
           CONVERT(varchar(20), f.OrgId) + '|' + f.FactType + '|RoryRound2-2026-09-08'), 2),
       f.OrgId, f.FactType, f.Body, f.SourceUrl,
       N'Rory Beirne feedback round 2, 2026-09-08',
       CAST('2026-09-08' AS datetimeoffset), N'High', SYSUTCDATETIME(), N'BrainDecompose-2026-09-08'
FROM #f f
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgFact e
                  WHERE e.CanonicalOrgId = f.OrgId AND e.FactType = f.FactType
                    AND e.CreatedBy = N'BrainDecompose-2026-09-08' AND e.RetiredAtUtc IS NULL);

SELECT 'facts written' AS Section;
SELECT f.CanonicalOrgId, LEFT(co.DisplayName, 32) AS Org, f.FactType, LEFT(f.Body, 58) AS Body
FROM opportunities.OrgFact f
JOIN opportunities.CanonicalOrg co ON co.Id = f.CanonicalOrgId
WHERE f.CreatedBy = N'BrainDecompose-2026-09-08' ORDER BY co.DisplayName;
GO
