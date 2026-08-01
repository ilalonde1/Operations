SET XACT_ABORT ON;
GO

-- Defense honing cleanup + EllisDon strategic canonical layering.
-- Triggered by 2026-06-08 defense-military-honing pass which surfaced:
--   - 4 misclassified MPIs (not defense)
--   - 2 dead MPIs (delivered or misclassified school)
--   - 1 unverifiable MPI (KOR internal data, possibly bad)
--   - 6 NEW discovered defense projects not yet in MPI
-- Plus identified EllisDon as a strategic target on par with Graham
-- Design Builders LP (m108) — they hold Cowichan Hospital, BC Cancer
-- Kamloops, Freedom Mobile Arch, TELUS Ocean (BC) AND Esquimalt JNCM,
-- CFB Cold Lake FFCP (defense).

BEGIN TRAN;

-- ==========================================================
-- PART 1: EllisDon canonical consolidation + strategic notes
-- ==========================================================
DECLARE @ellisdonParent BIGINT = 22257;     -- EllisDon Corporation (clean)
DECLARE @kineticParent BIGINT = 69019;      -- EllisDon Kinetic (clean)
DECLARE @infraParent BIGINT = 69026;        -- EllisDon Infrastructure (clean)
DECLARE @jvDupe BIGINT = 69204;             -- "EllisDon Kinetic, A Joint Venture" (dirty)
DECLARE @p3Dupe BIGINT = 72453;             -- "EllisDon Infrastructure (P3 DBFM...)" (dirty)

-- Repoint dirty-suffix dupes to clean parents
UPDATE opportunities.MajorProjectsInventory SET GeneralContractorCanonicalOrgId = @kineticParent
WHERE GeneralContractorCanonicalOrgId = @jvDupe;
PRINT 'MPI GC FK repointed (jv dupe -> Kinetic): ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.MajorProjectsInventory SET GeneralContractorCanonicalOrgId = @infraParent
WHERE GeneralContractorCanonicalOrgId = @p3Dupe;
PRINT 'MPI GC FK repointed (p3 dupe -> Infrastructure): ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE opportunities.OpportunityAwards SET AwardedToCanonicalOrgId = @kineticParent
WHERE AwardedToCanonicalOrgId = @jvDupe;
UPDATE opportunities.OpportunityAwards SET AwardedToCanonicalOrgId = @infraParent
WHERE AwardedToCanonicalOrgId = @p3Dupe;

UPDATE opportunities.IntelSignal SET CanonicalOrgId = @kineticParent WHERE CanonicalOrgId = @jvDupe;
UPDATE opportunities.IntelSignal SET CanonicalOrgId = @infraParent WHERE CanonicalOrgId = @p3Dupe;
UPDATE opportunities.IntelAction SET CanonicalOrgId = @kineticParent WHERE CanonicalOrgId = @jvDupe;
UPDATE opportunities.IntelAction SET CanonicalOrgId = @infraParent WHERE CanonicalOrgId = @p3Dupe;
UPDATE opportunities.IntelWork SET CanonicalOrgId = @kineticParent WHERE CanonicalOrgId = @jvDupe;
UPDATE opportunities.IntelWork SET CanonicalOrgId = @infraParent WHERE CanonicalOrgId = @p3Dupe;
UPDATE opportunities.IntelRisk SET CanonicalOrgId = @kineticParent WHERE CanonicalOrgId = @jvDupe;
UPDATE opportunities.IntelRisk SET CanonicalOrgId = @infraParent WHERE CanonicalOrgId = @p3Dupe;
UPDATE opportunities.IntelPersonAffiliation SET CanonicalOrgId = @kineticParent WHERE CanonicalOrgId = @jvDupe;
UPDATE opportunities.IntelPersonAffiliation SET CanonicalOrgId = @infraParent WHERE CanonicalOrgId = @p3Dupe;
UPDATE opportunities.IntelProjectKeyPerson SET CanonicalOrgId = @kineticParent WHERE CanonicalOrgId = @jvDupe;
UPDATE opportunities.IntelProjectKeyPerson SET CanonicalOrgId = @infraParent WHERE CanonicalOrgId = @p3Dupe;

UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'Merged into clean EllisDon canonicals via m109 — dirty descriptive suffix only'
WHERE Id IN (@jvDupe, @p3Dupe) AND RetiredAtUtc IS NULL;
PRINT 'EllisDon dupes retired: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- Strategic-target notes on EllisDon Corporation parent
UPDATE opportunities.CanonicalOrg
SET Kind = N'GC',
    Notes = COALESCE(Notes, N'') + CHAR(13) + CHAR(10) +
        N'[BD intel 2026-06-08] STRATEGIC TARGET on par with Graham Design Builders LP (canonical 8361). EllisDon BC pipeline: Cowichan District Hospital ($1B+, 9-storey, 204 beds, LEED Gold — Nuts''a''maat Alliance with Parkin + ZGF + Bush Bohlman structural), BC Cancer Centre Kamloops (with Interior Health expansion to Royal Inland), Freedom Mobile Arch Vancouver (10,000-seat mass timber canopy), TELUS Ocean Vancouver. Defense pipeline: Esquimalt JNCM Accommodations $165M ($10.1M design phase Aug 2024, structural sub NOT publicly named — KOR LIVE INTEL GAP), CFB Cold Lake FFCP FSF (Stantec captive structural — KOR no entry). EllisDon Kinetic + EllisDon Infrastructure + EllisDon Infrastructure Healthcare are subsidiaries. KOR target: Vancouver/Victoria pre-construction managers for live intel gap pursuit positioning.',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @ellisdonParent;
PRINT 'EllisDon Corporation 22257 updated with strategic context';

-- ==========================================================
-- PART 2: Re-categorize 4 misclassified MPIs (not defense)
-- ==========================================================
-- 3255 Esquimalt Triangle Site — First Nations commercial
UPDATE opportunities.MajorProjectsInventory
SET ProponentName = N'Esquimalt Nation / Kosapsum Development Corporation',
    KorSeatOpening = COALESCE(KorSeatOpening, N'') + CHAR(13) + CHAR(10) +
        N'[Defense honing 2026-06-08] Re-categorized OUT of defense pipeline. Esquimalt Nation reserve land development, not DND/DCC. Geographic proximity to CFB Esquimalt caused defense filter false positive.',
    Sector = N'Commercial / First Nations',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 3255 AND RetiredAtUtc IS NULL;
PRINT 'MPI 3255 re-categorized (Esquimalt Triangle): ' + CONVERT(varchar(20), @@ROWCOUNT);

-- 5180 Comox Valley Third Ice Surface — civic recreation, NOT defense
UPDATE opportunities.MajorProjectsInventory
SET ProponentName = N'Comox Valley Regional District',
    KorSeatOpening = COALESCE(KorSeatOpening, N'') + CHAR(13) + CHAR(10) +
        N'[Defense honing 2026-06-08] Re-categorized OUT of defense pipeline. CVRD civic recreation project, not DND/DCC. Honing flagged this as DUPLICATE of MPI 5287 — consolidate.',
    Sector = N'Civic / Recreation',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 5180 AND RetiredAtUtc IS NULL;
PRINT 'MPI 5180 re-categorized (CV Third Ice): ' + CONVERT(varchar(20), @@ROWCOUNT);

-- 5184 Comox Valley RCMP Detachment Replacement — civic policing
UPDATE opportunities.MajorProjectsInventory
SET ProponentName = N'City of Courtenay / Comox Valley RCMP',
    KorSeatOpening = COALESCE(KorSeatOpening, N'') + CHAR(13) + CHAR(10) +
        N'[Defense honing 2026-06-08] Re-categorized OUT of defense pipeline. RCMP detachment is municipal/provincial, not DND/DCC. ~$47M institutional structural opportunity if project advances to design RFP.',
    Sector = N'Civic / Policing',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 5184 AND RetiredAtUtc IS NULL;
PRINT 'MPI 5184 re-categorized (CV RCMP): ' + CONVERT(varchar(20), @@ROWCOUNT);

-- 5287 Comox Valley Arena 3 — duplicate of 5180, retire
UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'Dup of MPI 5180 CV Third Ice (defense honing 2026-06-08)',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 5287 AND RetiredAtUtc IS NULL;
PRINT 'MPI 5287 retired as duplicate of 5180: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ==========================================================
-- PART 3: Retire 2 dead MPIs
-- ==========================================================
-- 718 CFB Edmonton Health Sciences Centre — delivered 2019
UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'Delivered ~2019 per defense honing 2026-06-08. GC: Aman Building. DCC. No struct eng oppty.',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 718 AND RetiredAtUtc IS NULL;
PRINT 'MPI 718 retired (CFB Edmonton HSC delivered 2019): ' + CONVERT(varchar(20), @@ROWCOUNT);

-- 5444 Esquimalt Secondary — misclassified school
UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = sysdatetimeoffset(),
    RetiredReason = N'Misclassified as defense; actually SD61/MoE K-12 school per defense honing 2026-06-08.',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 5444 AND RetiredAtUtc IS NULL;
PRINT 'MPI 5444 retired (Esquimalt Secondary misclassified): ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ==========================================================
-- PART 4: Flag 6437 for investigation (don''t retire — needs Ian)
-- ==========================================================
UPDATE opportunities.MajorProjectsInventory
SET KorSeatOpening = COALESCE(KorSeatOpening, N'') + CHAR(13) + CHAR(10) +
        N'[Defense honing 2026-06-08] INVESTIGATE — no public procurement record, no government project data, no structural scope identified. Per honing brief: "KOR should investigate internally: who entered this record and what the actual underlying project is. If Bill Vanstein / Anthem Drywall reference is a CRM artifact, retire." Flagged for Ian review.',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 6437 AND RetiredAtUtc IS NULL;
PRINT 'MPI 6437 flagged for investigation: ' + CONVERT(varchar(20), @@ROWCOUNT);

-- ==========================================================
-- PART 5: Insert 6 discovered defense projects as new MPI rows
-- ==========================================================
DECLARE @now DATETIMEOFFSET = sysdatetimeoffset();

-- BC discoveries
INSERT INTO opportunities.MajorProjectsInventory
    (Province, SourceKey, ProjectName, ProjectDescription, ProjectStage, MunicipalityName, ProponentName, Sector,
     FirstSeenAtUtc, LastSeenAtUtc, UpdatedAtUtc, KorSeatOpening)
VALUES
    (N'BC', N'DEFENSE-DISC-2026-06-08-comox-cmma-hangar', N'19 Wing Comox CMMA Hangar',
     N'Multi-Mission Aircraft maintenance hangar at 19 Wing Comox, CFB Comox. Anticipated Spring 2025 procurement posting via DCC. Estimated $279M. Likely SECRET FSC clearance restricted.',
     N'Planned', N'Comox', N'Department of National Defence (via DCC)', N'Defense',
     @now, @now, @now,
     N'[Discovered 2026-06-08 via defense-military first-pass drain]. Estimated $279M. CMMA hangar at 19 Wing Comox. Likely SECRET FSC restricted — KOR clearance evaluation required before pursuit.'),

    (N'BC', N'DEFENSE-DISC-2026-06-08-esquimalt-cfha-120', N'CFB Esquimalt CFHA 120 Units',
     N'Canadian Forces Housing Agency (CFHA) 120-unit residential program at CFB Esquimalt. Mid-rise residential typology, KOR-competitive.',
     N'Planned', N'Esquimalt', N'Canadian Forces Housing Agency / Department of National Defence', N'Defense',
     @now, @now, @now,
     N'[Discovered 2026-06-08 via defense-military first-pass drain]. 120-unit residential program. CFHA is multi-year rollout pattern — landing one phase opens future phases. Reliability clearance likely sufficient.'),

    (N'BC', N'DEFENSE-DISC-2026-06-08-comox-cfha-housing-p2', N'19 Wing Comox CFHA Housing Phase 2',
     N'Canadian Forces Housing Agency (CFHA) Phase 2 housing rollout at 19 Wing Comox.',
     N'Planned', N'Comox', N'Canadian Forces Housing Agency / Department of National Defence', N'Defense',
     @now, @now, @now,
     N'[Discovered 2026-06-08 via defense-military first-pass drain]. Phase 2 of CFHA housing program — landing this opens Phase 3+.');

-- AB discoveries
INSERT INTO opportunities.MajorProjectsInventory
    (Province, SourceKey, ProjectName, ProjectDescription, ProjectStage, MunicipalityName, ProponentName, Sector,
     FirstSeenAtUtc, LastSeenAtUtc, UpdatedAtUtc, KorSeatOpening)
VALUES
    (N'AB', N'DEFENSE-DISC-2026-06-08-sttcp-mob-west-edmonton', N'STTCP MOB West Edmonton',
     N'Strategic Tactical Airlift Capability Project (STTCP) Mobile Operations Base West — new facility at CFB Edmonton.',
     N'Planned', N'Edmonton', N'Department of National Defence (via DCC)', N'Defense',
     @now, @now, @now,
     N'[Discovered 2026-06-08 via defense-military first-pass drain]. STTCP MOB West — Edmonton-area defense facility. KOR Alberta presence is the angle.'),

    (N'AB', N'DEFENSE-DISC-2026-06-08-drdc-suffield-lab', N'DRDC Suffield Lab Expansion',
     N'Defence Research and Development Canada (DRDC) laboratory expansion at Suffield, Alberta. Research facility — specialty structural requirements likely.',
     N'Planned', N'Suffield', N'Defence Research and Development Canada / Department of National Defence', N'Defense',
     @now, @now, @now,
     N'[Discovered 2026-06-08 via defense-military first-pass drain]. DRDC Suffield laboratory expansion. Research-facility specialty structural is KOR competitive angle.'),

    (N'AB', N'DEFENSE-DISC-2026-06-08-edmonton-cfha-housing-p2', N'CFB Edmonton CFHA Housing Phase 2',
     N'Canadian Forces Housing Agency (CFHA) Phase 2 housing rollout at CFB Edmonton. Multi-year rollout pattern.',
     N'Planned', N'Edmonton', N'Canadian Forces Housing Agency / Department of National Defence', N'Defense',
     @now, @now, @now,
     N'[Discovered 2026-06-08 via defense-military first-pass drain]. CFHA Phase 2 at CFB Edmonton. KOR Alberta presence + mid-rise residential = direct fit. Reliability clearance likely sufficient.');

PRINT 'Discovered defense projects inserted: 6';

PRINT 'Migration 109 complete.';

COMMIT TRAN;
GO

-- Verify
SELECT 'EllisDon canonicals after consolidation' AS K, Id, DisplayName, Kind, RetiredAtUtc
FROM opportunities.CanonicalOrg WHERE DisplayName LIKE N'%EllisDon%' ORDER BY Id;

SELECT 'Defense MPI state' AS K, COUNT(*) AS Cnt FROM opportunities.MajorProjectsInventory
WHERE Province IN (N'BC',N'AB') AND RetiredAtUtc IS NULL
  AND (ProponentName LIKE N'%Defence%' OR ProponentName LIKE N'%CFHA%' OR ProponentName LIKE N'%DCC%' OR Sector = N'Defense');

SELECT 'Newly inserted defense MPIs' AS K, Id, ProjectName, Province, ProponentName
FROM opportunities.MajorProjectsInventory
WHERE SourceKey LIKE N'DEFENSE-DISC-2026-06-08-%'
ORDER BY Province, Id;
GO
