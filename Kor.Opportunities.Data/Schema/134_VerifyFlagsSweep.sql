-- 134_VerifyFlagsSweep.sql
-- Verify-flags evidence pass over docs/BD-SummaryFlags-Worklist-2026-06-12.md.
-- 33-item QueueDrain verify campaign (verify-flags, drained 2026-06-12, web
-- evidence per item in outputs/verify-N.json); every record re-verified
-- against the live DB before inclusion. Merge-class outcomes (12 pairs) are
-- NOT here — they go through BdCanonicalDedup --pairs (FK repoints).
-- Items deliberately EXCLUDED, with reasons:
--   * verify-14 Lemco(10869)/Lemay(10868): different award contracts (Calgary
--     20-1651 vs AB Infra 013065K), no shared contacts, no web evidence a firm
--     'Lemco Architecture' exists — same-entity unproven, left for manual call.
--   * verify-17 ATB/CTA (2751 'ATB Architecture'): CTA Architecture + Design
--     Ltd. bankruptcy is real (MNP, Court of King's Bench 2503 00405) but no
--     evidence 2751 is that firm — 'ATB' in the flag was the CREDITOR (ATB
--     Financial). 2751's only link is a 2015 Edmonton award naming 'ATB
--     Architecture' verbatim. Identity unproven; not retired.
--   * verify-7 Isle Energy Consulting Ltd. NOT created: Part 9 residential
--     energy consulting, outside KOR's BD scope (Part 3 structural).
--   * verify-30 Sammi Ha: her IntelProjectKeyPerson row (261) is already
--     retired; no IntelPerson record exists. Nothing to update.
--   * verify-28 'create Dermot Kelly': already exists (IntelPerson 2662 with
--     current Fraser Health affiliations). Nothing to create.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRAN;

DECLARE @now datetimeoffset = sysdatetimeoffset();

------------------------------------------------------------------------
-- PART 1: org renames (rebrands verified by primary sources)
------------------------------------------------------------------------

-- verify-1: SNC-Lavalin -> AtkinsRéalis (rebrand 2023-09-12, atkinsrealis.com
-- press release + Bloomberg). 15558 is the BD Competitor record; the
-- AtkinsRealis Deltek-vendor sprawl (2775/42578/48796/52349/64052/64083)
-- stays for the deferred awards-tier cleanup.
UPDATE opportunities.CanonicalOrg
SET DisplayName = N'AtkinsRéalis', Website = N'https://www.atkinsrealis.com/', UpdatedAtUtc = @now
WHERE Id = 15558 AND DisplayName = N'SNC Lavalin Inc.' AND RetiredAtUtc IS NULL;
PRINT 'verify-1 AtkinsRéalis: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-2: Ivanhoé Cambridge brand dissolved into La Caisse (CDPQ announce
-- 2025-06-16, CoStar/PERE). Survivor 38947 renamed here; dup 53516 merges
-- into it via the dedup pairs run that follows this migration.
UPDATE opportunities.CanonicalOrg
SET DisplayName = N'La Caisse (CDPQ)', UpdatedAtUtc = @now
WHERE Id = 38947 AND DisplayName = N'Ivanhoé Cambridge (now operating as La Caisse / CDPQ Real Estate)' AND RetiredAtUtc IS NULL;
PRINT 'verify-2 La Caisse: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-3: Hokanson Capital -> HCI Ventures Inc (hokansoncapital.ca 301s to
-- hci.ca; site title/footer 'HCI Ventures Inc').
UPDATE opportunities.CanonicalOrg
SET DisplayName = N'HCI Ventures Inc.', Website = N'https://www.hci.ca/', UpdatedAtUtc = @now
WHERE Id = 53335 AND DisplayName = N'Hokanson Capital' AND RetiredAtUtc IS NULL;
PRINT 'verify-3 HCI Ventures: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-4: Points West Living -> Connecting Care (connectingcare.ca:
-- communities 'owned and/or operated by Connecting Care'; pointswestliving.com
-- now serves Connecting Care content).
UPDATE opportunities.CanonicalOrg
SET DisplayName = N'Connecting Care', Website = N'https://connectingcare.ca/', UpdatedAtUtc = @now
WHERE Id = 53491 AND DisplayName = N'Points West Living Inc.' AND RetiredAtUtc IS NULL;
PRINT 'verify-4 Connecting Care: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-5: Twin Peaks Engineering -> Ikon Engineers ('Ikon Engineers,
-- formerly Twin Peaks Engineering' on ikonengineers.com/about; Whistler
-- Chamber listing resolves to Ikon Engineers Canada Ltd.). Website already
-- ikonengineers.com on the row.
UPDATE opportunities.CanonicalOrg
SET DisplayName = N'Ikon Engineers', UpdatedAtUtc = @now
WHERE Id = 70731 AND DisplayName = N'Twin Peaks Structural Engineering' AND RetiredAtUtc IS NULL;
PRINT 'verify-5 Ikon Engineers: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-6: Egis Canada consolidation. 74130 ('CCI Group Inc. (CCIG) — now
-- operating as Egis Canada (formerly McIntosh Perry)', m126-suppressed)
-- becomes the canonical Egis Canada record; McIntosh Perry rows 11533/11534
-- merge into it via the dedup pairs run. Kind goes to Unknown with an
-- allied-discipline suppression, mirroring m132's treatment of the McIntosh
-- Perry records (Egis Canada = transportation/civil/building-science
-- multidiscipline, not a structural competitor). Vendor row 6768 'Egis
-- Canada Ltd.' (no normalized collision: 'egiscanadaltd' vs 'egiscanada')
-- stays for the awards-tier cleanup.
UPDATE opportunities.CanonicalOrg
SET DisplayName = N'Egis Canada', Kind = N'Unknown',
    EnrichmentSuppressedAtUtc = @now,
    EnrichmentSuppressedReason = N'm134: allied-discipline (non-structural) — multidiscipline successor of McIntosh Perry + CCI Group, m132-consistent',
    UpdatedAtUtc = @now
WHERE Id = 74130 AND DisplayName = N'CCI Group Inc. (CCIG) — now operating as Egis Canada (formerly McIntosh Perry)' AND RetiredAtUtc IS NULL;
PRINT 'verify-6 Egis Canada: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-7: Method Engineering split (methodengineering.ca: 'Method
-- Engineering is now Metachro Engineering & Construction Management and Isle
-- Energy Consulting Ltd.'). 47266 follows the building-envelope successor.
UPDATE opportunities.CanonicalOrg
SET DisplayName = N'Metachro Engineering & Construction Management', Website = N'https://www.metachro.ca/', UpdatedAtUtc = @now
WHERE Id = 47266 AND DisplayName = N'Method Engineering & Building Services Ltd.' AND RetiredAtUtc IS NULL;
PRINT 'verify-7 Metachro: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-9: E. Holland Contracting -> Holland Power Services (Alectra
-- 2021-01-05). Existing active 50504 'Holland Power Services Inc.' makes this
-- a MERGE (46917 -> 50504) — handled in the dedup pairs run, not here.

------------------------------------------------------------------------
-- PART 2: fused buyer-row surgery (verify-33)
-- 38998/39053/39073 are FUSED records: enrichment attached IHA/SD23/SD73
-- identities (websites, aliases, MPI links) onto unrelated vendor rows.
-- Merging would pollute the survivors with wrong-entity names, so the
-- foreign identity is surgically repointed instead.
------------------------------------------------------------------------

-- 38998 'Standards Council of Canada': 4 awards are genuinely SCC, but the
-- website (interiorhealth.ca), the 'Interior Health Authority' alias, all 15
-- MPI links (Penticton Regional, Royal Inland, Kelowna GH ... — every one an
-- IHA capital project) and the Brian Miller 'Director, Capital Planning &
-- Procurement' affiliation belong to Interior Health (survivor 54977,
-- mpi=68, the established IHA canonical).
UPDATE opportunities.MajorProjectsInventory SET ArchitectCanonicalOrgId = 54977, UpdatedAtUtc = @now WHERE ArchitectCanonicalOrgId = 38998;
UPDATE opportunities.MajorProjectsInventory SET StructuralEngineerCanonicalOrgId = 54977, UpdatedAtUtc = @now WHERE StructuralEngineerCanonicalOrgId = 38998;
UPDATE opportunities.MajorProjectsInventory SET ProponentCanonicalOrgId = 54977, UpdatedAtUtc = @now WHERE ProponentCanonicalOrgId = 38998;
PRINT 'verify-33 38998 MPI repoints done';

UPDATE al SET CanonicalOrgId = 54977
FROM opportunities.OrgAlias al
WHERE al.CanonicalOrgId = 38998 AND al.RawName = N'Interior Health Authority'
  AND NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias x WHERE x.CanonicalOrgId = 54977 AND x.RawName = al.RawName);
DELETE FROM opportunities.OrgAlias WHERE CanonicalOrgId = 38998 AND RawName = N'Interior Health Authority';
PRINT 'verify-33 38998 IHA alias repointed';

UPDATE opportunities.IntelPersonAffiliation SET CanonicalOrgId = 54977, UpdatedAtUtc = @now
WHERE CanonicalOrgId = 38998 AND IntelPersonId = 7303 AND RetiredAtUtc IS NULL; -- Brian Miller, Director Capital Planning & Procurement (IHA role)
PRINT 'verify-33 Brian Miller affiliation -> 54977: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE opportunities.CanonicalOrg SET Website = NULL, UpdatedAtUtc = @now
WHERE Id = 38998 AND Website = N'https://www.interiorhealth.ca';
PRINT 'verify-33 38998 wrong website cleared: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 39053 'Province of New Brunswic' (typo'd shell): 4 of its 5 aliases are
-- SD23 variants and its single MPI (4422 George Pringle Secondary, West
-- Kelowna) is an SD23 project -> repoint to 54415 'School District 23'
-- (mpi=27, established survivor), then retire the shell (zero remaining
-- links, garbled name, KOR does no NB work).
UPDATE opportunities.MajorProjectsInventory SET ArchitectCanonicalOrgId = 54415, UpdatedAtUtc = @now WHERE ArchitectCanonicalOrgId = 39053;
UPDATE opportunities.MajorProjectsInventory SET StructuralEngineerCanonicalOrgId = 54415, UpdatedAtUtc = @now WHERE StructuralEngineerCanonicalOrgId = 39053;
UPDATE opportunities.MajorProjectsInventory SET ProponentCanonicalOrgId = 54415, UpdatedAtUtc = @now WHERE ProponentCanonicalOrgId = 39053;

UPDATE al SET CanonicalOrgId = 54415
FROM opportunities.OrgAlias al
WHERE al.CanonicalOrgId = 39053 AND al.RawName <> N'Province of New Brunswic'
  AND NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias x WHERE x.CanonicalOrgId = 54415 AND x.RawName = al.RawName);
DELETE FROM opportunities.OrgAlias WHERE CanonicalOrgId = 39053 AND RawName <> N'Province of New Brunswic';

UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = @now,
    RetiredReason = N'm134: fused/garbled shell — SD23 aliases + George Pringle MPI repointed to 54415; ''Province of New Brunswic'' is a typo''d vendor name with no remaining links (verify-flags #33)',
    Website = NULL, UpdatedAtUtc = @now
WHERE Id = 39053 AND RetiredAtUtc IS NULL;
PRINT 'verify-33 39053 repointed + retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 39073 'ABC Life Literacy Canada': real entity (its award row is ABC LIFE
-- LITERACY CANADA) wearing a wrong SD73 alias + website. Repoint the alias
-- to 18900 'School District 73 Kamloops - Thompson', clear the website, and
-- set Kind=Vendor (its only real-world link is an awarded contract; it is
-- not a construction Buyer).
UPDATE al SET CanonicalOrgId = 18900
FROM opportunities.OrgAlias al
WHERE al.CanonicalOrgId = 39073 AND al.RawName = N'School District No. 73 (Kamloops-Thompson)'
  AND NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias x WHERE x.CanonicalOrgId = 18900 AND x.RawName = al.RawName);
DELETE FROM opportunities.OrgAlias WHERE CanonicalOrgId = 39073 AND RawName = N'School District No. 73 (Kamloops-Thompson)';

UPDATE opportunities.CanonicalOrg SET Website = NULL, Kind = N'Vendor', UpdatedAtUtc = @now
WHERE Id = 39073 AND Website = N'https://www.sd73.bc.ca' AND RetiredAtUtc IS NULL;
PRINT 'verify-33 39073 SD73 alias repointed, website cleared: ' + CAST(@@ROWCOUNT AS varchar(10));

------------------------------------------------------------------------
-- PART 3: project key-person corrections (IntelProjectKeyPerson)
------------------------------------------------------------------------

-- verify-28: Dr. Victoria Lee departed Fraser Health 2025-02-19 (mutual
-- agreement; fraservalleytoday.ca, canhealth.com). Successor Dermot Kelly
-- already exists (IntelPerson 2662). Retire her still-active key-person rows.
UPDATE opportunities.IntelProjectKeyPerson
SET RetiredAtUtc = @now,
    RetiredReason = N'm134: departed Fraser Health 2025-02-19; successor Dermot Kelly (verify-flags #28)',
    UpdatedAtUtc = @now
WHERE Id IN (2937, 2938, 2940, 3943, 4142, 4143, 4144, 6117) AND RetiredAtUtc IS NULL;
PRINT 'verify-28 Victoria Lee rows retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-29: Doug McPhee on MPI 5117 (Fernie Elementary Replacement, SD5)
-- has two conflicting active rows: 1530 'Board Chair' and 5157
-- 'Secretary-Treasurer'. The honing flag says Secretary-Treasurer is wrong;
-- the Board Chair row corroborates. Retire the wrong-title row, keep 1530.
UPDATE opportunities.IntelProjectKeyPerson
SET RetiredAtUtc = @now,
    RetiredReason = N'm134: wrong title — flagged not-Secretary-Treasurer; Board Chair row 1530 stands (verify-flags #29)',
    UpdatedAtUtc = @now
WHERE Id = 5157 AND RetiredAtUtc IS NULL;
PRINT 'verify-29 McPhee wrong-title row retired: ' + CAST(@@ROWCOUNT AS varchar(10));

------------------------------------------------------------------------
-- PART 4: person/affiliation corrections (IntelPerson graph)
------------------------------------------------------------------------

-- verify-20: Karen Marler (1669) -> Principal Emeritus at hcma since Jan 2026
-- (hcma.ca/celebrating-karen-marler, REMI Network). No selection authority.
UPDATE opportunities.IntelPersonAffiliation
SET Title = N'Principal Emeritus',
    Notes = N'm134: emeritus role since 2026-01 — design guidance/mentorship only, no project-selection authority (verify-flags #20)',
    UpdatedAtUtc = @now
WHERE IntelPersonId = 1669 AND CanonicalOrgId = 8799 AND IsCurrent = 1 AND RetiredAtUtc IS NULL;
PRINT 'verify-20 Marler title: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-21: Atila Zekioglu (288) joined Degenkolb LA as Senior Principal
-- 2024-02-01 (degenkolb.com press release). A correct current Degenkolb
-- affiliation already exists; fix the two stale Saiful Bouquet (38933) rows:
-- the one with Degenkolb in the title is a wrong-org duplicate (retire),
-- the plain one is true history (demote to not-current).
UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = @now, RetiredReason = N'm134: wrong-org duplicate — Degenkolb title on Saiful Bouquet org row (verify-flags #21)', UpdatedAtUtc = @now
WHERE IntelPersonId = 288 AND CanonicalOrgId = 38933 AND Title LIKE N'%Degenkolb%' AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPersonAffiliation
SET IsCurrent = 0, EndDateApprox = N'2024-01', UpdatedAtUtc = @now
WHERE IntelPersonId = 288 AND CanonicalOrgId = 38933 AND RetiredAtUtc IS NULL AND IsCurrent = 1;
PRINT 'verify-21 Zekioglu affiliations fixed';

-- verify-22: Michael Liou (290) -> Degenkolb Associate Principal
-- (degenkolb.com/bios/michael-liou, LinkedIn). Same two-row fix as Zekioglu,
-- plus mint the missing current Degenkolb affiliation (68610).
UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = @now, RetiredReason = N'm134: wrong-org duplicate — Degenkolb title on Saiful Bouquet org row (verify-flags #22)', UpdatedAtUtc = @now
WHERE IntelPersonId = 290 AND CanonicalOrgId = 38933 AND Title LIKE N'%Degenkolb%' AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPersonAffiliation
SET IsCurrent = 0, EndDateApprox = N'2024', UpdatedAtUtc = @now
WHERE IntelPersonId = 290 AND CanonicalOrgId = 38933 AND RetiredAtUtc IS NULL AND IsCurrent = 1;

DECLARE @anchor68610 bigint;
SELECT @anchor68610 = Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = 68610 AND ProviderName = N'VerifyFlags-2026-06-12';
IF @anchor68610 IS NULL
BEGIN
    INSERT INTO opportunities.CanonicalOrgEnrichment (CanonicalOrgId, ProviderName, Status, LastRefreshAtUtc, LastAttemptAtUtc, Attempts, ResultJson, Notes, CreatedAtUtc, UpdatedAtUtc)
    VALUES (68610, N'VerifyFlags-2026-06-12', N'ok', @now, @now, 1, NULL, N'Anchor enrichment for m134 verify-flags affiliation mints.', @now, @now);
    SET @anchor68610 = SCOPE_IDENTITY();
END
IF NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation WHERE IntelPersonId = 290 AND CanonicalOrgId = 68610 AND RetiredAtUtc IS NULL)
BEGIN
    INSERT INTO opportunities.IntelPersonAffiliation (IntelPersonId, CanonicalOrgId, Title, IsCurrent, StartDateApprox, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey)
    VALUES (290, 68610, N'Associate Principal', 1, N'2024', @now, @now, @now, @now, N'KorBdVerifyFlags-2026-06-12', @anchor68610, N'Manual-2026-06-12', N'michael-liou-degenkolb-m134');
    PRINT 'verify-22 Liou Degenkolb affiliation minted';
END

-- verify-23 (REFUTED claim): Andrew Lischuk (248) did NOT return to Stantec —
-- Principal, Structural Engineer at Eng-Spire since 2020-06 (LinkedIn).
-- Strip the speculation from the title; the Eng-Spire org dup (71169->68740)
-- is handled in the dedup pairs run.
UPDATE opportunities.IntelPersonAffiliation
SET Title = N'Principal, Structural Engineer', UpdatedAtUtc = @now
WHERE IntelPersonId = 248 AND CanonicalOrgId = 68740 AND Title = N'Principal, Structural Engineer (may have departed to Stantec)' AND RetiredAtUtc IS NULL;
PRINT 'verify-23 Lischuk title cleaned: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-24: Brad Klassen (481) is Chief GOVERNANCE Officer at Troika since
-- 2024-11-01 (troikadevelopments.com leadership announcement); Jeff Kennedy
-- was promoted to CFO the same day. Of Klassen's three active 'current'
-- rows at 54033: fix the CGO row's wrong expansion (Growth->Governance),
-- retire the stale CFO row and the editorial 'NOT CFO' noise row.
UPDATE opportunities.IntelPersonAffiliation
SET Title = N'Chief Governance Officer (CGO)', UpdatedAtUtc = @now
WHERE IntelPersonId = 481 AND CanonicalOrgId = 54033 AND Title = N'CGO (Chief Growth Officer), Troika Developments' AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = @now, RetiredReason = N'm134: CFO role passed to Jeff Kennedy 2024-11-01 (verify-flags #24)', UpdatedAtUtc = @now
WHERE IntelPersonId = 481 AND CanonicalOrgId = 54033 AND Title = N'CFO' AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = @now, RetiredReason = N'm134: editorial-noise duplicate of the corrected CGO row (verify-flags #24)', UpdatedAtUtc = @now
WHERE IntelPersonId = 481 AND CanonicalOrgId = 54033 AND Title LIKE N'%NOT CFO%' AND RetiredAtUtc IS NULL;
PRINT 'verify-24 Klassen rows fixed';

DECLARE @anchor54033 bigint, @kennedyId bigint;
SELECT @anchor54033 = Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = 54033 AND ProviderName = N'VerifyFlags-2026-06-12';
IF @anchor54033 IS NULL
BEGIN
    INSERT INTO opportunities.CanonicalOrgEnrichment (CanonicalOrgId, ProviderName, Status, LastRefreshAtUtc, LastAttemptAtUtc, Attempts, ResultJson, Notes, CreatedAtUtc, UpdatedAtUtc)
    VALUES (54033, N'VerifyFlags-2026-06-12', N'ok', @now, @now, 1, NULL, N'Anchor enrichment for m134 verify-flags affiliation mints.', @now, @now);
    SET @anchor54033 = SCOPE_IDENTITY();
END
IF NOT EXISTS (SELECT 1 FROM opportunities.IntelPerson WHERE DisplayName = N'Jeff Kennedy' AND RetiredAtUtc IS NULL)
BEGIN
    INSERT INTO opportunities.IntelPerson (DisplayName, NormalizedName, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, Notes, Corroborations, SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey)
    VALUES (N'Jeff Kennedy', N'jeff kennedy', @now, @now, @now, @now,
        N'CFO, Troika Management Corp. (Kelowna) — promoted 2024-11-01 in the leadership change that moved Brad Klassen to CGO. Source: troikadevelopments.com 2024-12-12 announcement (m134 verify-flags #24).',
        1, N'KorBdVerifyFlags-2026-06-12', @anchor54033, N'Manual-2026-06-12', N'jeff-kennedy-troika-kelowna');
    SET @kennedyId = SCOPE_IDENTITY();
    INSERT INTO opportunities.IntelPersonAffiliation (IntelPersonId, CanonicalOrgId, Title, IsCurrent, StartDateApprox, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey)
    VALUES (@kennedyId, 54033, N'Chief Financial Officer (CFO)', 1, N'2024-11', @now, @now, @now, @now, N'KorBdVerifyFlags-2026-06-12', @anchor54033, N'Manual-2026-06-12', N'jeff-kennedy-troika-m134');
    PRINT 'verify-24 Jeff Kennedy minted with Id: ' + CAST(@kennedyId AS varchar(12));
END

-- verify-25: Gudrun Seredynski (484) is VP, Real Estate Accounting at
-- Strategic Group (zoominfo + strategicgroup.ca/executive page), not CFO
-- (CFO was her prior NORCAL Mutual role). Two duplicate current rows:
-- correct one, retire the other.
UPDATE opportunities.IntelPersonAffiliation
SET Title = N'VP, Real Estate Accounting', UpdatedAtUtc = @now
WHERE IntelPersonId = 484 AND CanonicalOrgId = 38954 AND Title = N'Chief Financial Officer' AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = @now, RetiredReason = N'm134: duplicate row; corrected sibling stands (verify-flags #25)', UpdatedAtUtc = @now
WHERE IntelPersonId = 484 AND CanonicalOrgId = 38954 AND Title = N'Chief Financial Officer, Strategic Group' AND RetiredAtUtc IS NULL;
PRINT 'verify-25 Seredynski fixed';

-- verify-26: Laurie Anderson (477) — ONE Properties entered receivership
-- 2025-09-24 (westerninvestor.com); current employer unknown. Demote both
-- 'current' rows; keep history.
UPDATE opportunities.IntelPersonAffiliation
SET IsCurrent = 0, EndDateApprox = N'2025-09',
    Notes = N'm134: ONE Properties receivership 2025-09-24; current employer unknown — follow up (verify-flags #26)',
    UpdatedAtUtc = @now
WHERE IntelPersonId = 477 AND CanonicalOrgId IN (38948, 53270) AND IsCurrent = 1 AND RetiredAtUtc IS NULL;
PRINT 'verify-26 Anderson demoted: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-27: Darren Durstling (476) -> President, Integro Investments
-- (integroinvestments.ca; Jan 2026 Wiikwemkoong/Connect Centre JV release).
-- Mint Integro Investments (no existing record; 60687 'Integro Comunicacion
-- Visual S.A.' is an unrelated vendor), demote the ONE Properties/WAM rows,
-- retire the narrative-noise Katz Group row.
DECLARE @integroId bigint;
SELECT @integroId = Id FROM opportunities.CanonicalOrg WHERE NormalizedName = N'integroinvestments' AND RetiredAtUtc IS NULL;
IF @integroId IS NULL
BEGIN
    INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Website, Notes, CreatedAtUtc, UpdatedAtUtc)
    VALUES (N'Developer', N'Integro Investments', N'https://integroinvestments.ca/',
        N'[m134: minted for Darren Durstling''s post-ONE-Properties role — President. Edmonton-based investment/development firm; JV partner with Wiikwemkoong First Nation on Connect Centre (Jan 2026 release). verify-flags #27]',
        @now, @now);
    SET @integroId = SCOPE_IDENTITY();
    PRINT 'verify-27 Integro Investments minted with Id: ' + CAST(@integroId AS varchar(12));
END

DECLARE @anchorIntegro bigint;
SELECT @anchorIntegro = Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = @integroId AND ProviderName = N'VerifyFlags-2026-06-12';
IF @anchorIntegro IS NULL
BEGIN
    INSERT INTO opportunities.CanonicalOrgEnrichment (CanonicalOrgId, ProviderName, Status, LastRefreshAtUtc, LastAttemptAtUtc, Attempts, ResultJson, Notes, CreatedAtUtc, UpdatedAtUtc)
    VALUES (@integroId, N'VerifyFlags-2026-06-12', N'ok', @now, @now, 1, NULL, N'Anchor enrichment for m134 verify-flags affiliation mints.', @now, @now);
    SET @anchorIntegro = SCOPE_IDENTITY();
END
IF NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation WHERE IntelPersonId = 476 AND CanonicalOrgId = @integroId AND RetiredAtUtc IS NULL)
BEGIN
    INSERT INTO opportunities.IntelPersonAffiliation (IntelPersonId, CanonicalOrgId, Title, IsCurrent, StartDateApprox, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey)
    VALUES (476, @integroId, N'President', 1, N'2025', @now, @now, @now, @now, N'KorBdVerifyFlags-2026-06-12', @anchorIntegro, N'Manual-2026-06-12', N'darren-durstling-integro-m134');
    PRINT 'verify-27 Durstling Integro affiliation minted';
END

UPDATE opportunities.IntelPersonAffiliation
SET IsCurrent = 0, EndDateApprox = N'2025-09', UpdatedAtUtc = @now
WHERE IntelPersonId = 476 AND CanonicalOrgId IN (38948, 53270) AND IsCurrent = 1 AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = @now, RetiredReason = N'm134: narrative-noise row (''Former CEO ... departed at bankruptcy'' as a title) (verify-flags #27)', UpdatedAtUtc = @now
WHERE IntelPersonId = 476 AND CanonicalOrgId = 53422 AND RetiredAtUtc IS NULL;
PRINT 'verify-27 Durstling old rows demoted/retired';

-- verify-31: Tim McLennan (IntelPerson 80) is CEO of Faction Projects
-- (70546, current affiliation already present). He departed HDR — demote
-- the stale HDR 'Partner & Director, Kelowna Operations' current row.
UPDATE opportunities.IntelPersonAffiliation
SET IsCurrent = 0, UpdatedAtUtc = @now
WHERE IntelPersonId = 80 AND CanonicalOrgId = 70635 AND IsCurrent = 1 AND RetiredAtUtc IS NULL;
PRINT 'verify-31 McLennan HDR row demoted: ' + CAST(@@ROWCOUNT AS varchar(10));

-- verify-32: Danny Wolsey sold Wolsey Structural (dwse.ca: 'previous company
-- that he owned and operated for 17 years before selling') and founded DW
-- Structural Engineering Ltd. Mint the org, add the current affiliation on
-- person 244, demote his Wolsey Structural rows, and retire dup person 9099
-- ('Daniel (Danny) Wolsey', single redundant affiliation).
DECLARE @dwId bigint;
SELECT @dwId = Id FROM opportunities.CanonicalOrg WHERE NormalizedName = N'dwstructuralengineeringltd' AND RetiredAtUtc IS NULL;
IF @dwId IS NULL
BEGIN
    INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Website, Notes, CreatedAtUtc, UpdatedAtUtc)
    VALUES (N'Competitor', N'DW Structural Engineering Ltd.', N'https://www.dwse.ca/',
        N'[m134: minted — Danny Wolsey''s new independent practice after selling Wolsey Structural Engineering Ltd. (68753, still active separately). Calgary AB. verify-flags #32]',
        @now, @now);
    SET @dwId = SCOPE_IDENTITY();
    PRINT 'verify-32 DW Structural minted with Id: ' + CAST(@dwId AS varchar(12));
END

DECLARE @anchorDw bigint;
SELECT @anchorDw = Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = @dwId AND ProviderName = N'VerifyFlags-2026-06-12';
IF @anchorDw IS NULL
BEGIN
    INSERT INTO opportunities.CanonicalOrgEnrichment (CanonicalOrgId, ProviderName, Status, LastRefreshAtUtc, LastAttemptAtUtc, Attempts, ResultJson, Notes, CreatedAtUtc, UpdatedAtUtc)
    VALUES (@dwId, N'VerifyFlags-2026-06-12', N'ok', @now, @now, 1, NULL, N'Anchor enrichment for m134 verify-flags affiliation mints.', @now, @now);
    SET @anchorDw = SCOPE_IDENTITY();
END
IF NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation WHERE IntelPersonId = 244 AND CanonicalOrgId = @dwId AND RetiredAtUtc IS NULL)
BEGIN
    INSERT INTO opportunities.IntelPersonAffiliation (IntelPersonId, CanonicalOrgId, Title, IsCurrent, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey)
    VALUES (244, @dwId, N'Founder & Principal', 1, @now, @now, @now, @now, N'KorBdVerifyFlags-2026-06-12', @anchorDw, N'Manual-2026-06-12', N'danny-wolsey-dwstructural-m134');
    PRINT 'verify-32 Wolsey DW affiliation minted';
END

UPDATE opportunities.IntelPersonAffiliation
SET IsCurrent = 0, UpdatedAtUtc = @now
WHERE IntelPersonId = 244 AND CanonicalOrgId = 68753 AND IsCurrent = 1 AND RetiredAtUtc IS NULL;

UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = @now, RetiredReason = N'm134: affiliation of duplicate person 9099 (kept person = 244) (verify-flags #32)', UpdatedAtUtc = @now
WHERE IntelPersonId = 9099 AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPerson
SET RetiredAtUtc = @now, RetiredReason = N'm134: duplicate of IntelPerson 244 Daniel Wolsey (verify-flags #32)', UpdatedAtUtc = @now
WHERE Id = 9099 AND RetiredAtUtc IS NULL;
PRINT 'verify-32 Wolsey rows fixed';

------------------------------------------------------------------------
-- PART 5: retires
------------------------------------------------------------------------

-- verify-18: Minerva for Engineering Studies (60194) is a Jordanian
-- consultancy (LinkedIn: Khalil Al Salem St, Amman JO 11181) — data error,
-- no Canadian BD relevance.
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = @now,
    RetiredReason = N'm134: wrong jurisdiction — Jordanian firm (Amman), data error (verify-flags #18)',
    UpdatedAtUtc = @now
WHERE Id = 60194 AND RetiredAtUtc IS NULL;
PRINT 'verify-18 Minerva retired: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm134 committed.';
GO

-- Verify block
SELECT Id, DisplayName, Kind, Website, RetiredAtUtc, EnrichmentSuppressedReason
FROM opportunities.CanonicalOrg
WHERE Id IN (15558, 38947, 53335, 53491, 70731, 74130, 47266, 38998, 39053, 39073, 60194)
ORDER BY Id;
SELECT Id, DisplayName, RetiredAtUtc FROM opportunities.IntelPerson WHERE Id IN (9099) OR DisplayName = N'Jeff Kennedy';
GO
