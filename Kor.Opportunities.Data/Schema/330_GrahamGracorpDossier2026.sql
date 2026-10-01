-- Migration 330 (2026-10-01): the Graham / Gracorp file, corrected and extended.
--
-- Written while building docs/dossiers/KOR-Dossier-Graham-Gracorp-2026-10-01-web.pdf
-- for Conor Murtagh, who is at Graham's partner appreciation event tonight. Everything
-- here was found by checking the record we already held against Deltek and public
-- sources, and three parts of it were WRONG in a way that would have embarrassed him.
--
-- ⚠ THE DEFECT THAT MATTERS — a Yurkovich-class error, still live in the data.
--    Two records told us to go and ask Graham whether the structural seat on the
--    $1.96B Richmond Hospital Yurkovich Family Pavilion was open:
--      · IntelSignal  (2025-02, FirmNarrativeHoning) "SE slot may be open ... call
--        Graham BC (604-940-4500) immediately to confirm SE allocation"
--      · IntelNarrative Action (2026-06-20) "KOR should ask Graham if their SE
--        sub-consultant position is open"
--    It is not open. ReNew Canada's own project record for the redevelopment lists the
--    Engineer as ENTUITIVE, with HDR as architect and Graham Design Builders LP as the
--    Phase 2 alliance partner. This is the exact fallacy the Yurkovich rule names: on an
--    alliance / P3 / progressive design-build job the structural sub is locked at team
--    selection and alliances do not announce subs, so "no SE is named publicly" is the
--    NORMAL STATE OF A CLOSED TEAM, not evidence of an opening.
--
--    Note what changed since that rule was written: the Entuitive fact used to be
--    KOR-internal and un-sourceable. It is now public, so the correction below can cite
--    a URL rather than asserting from memory.
--
-- Also corrected here:
--   · "family-owned GC" — Graham began that way in 1926 and has been EMPLOYEE-OWNED
--     since the early 1980s, 100% so today. Our own Gracorp narrative already said
--     "employee-owned", so the file contradicted itself.
--   · Lee Holland and Jordan Hood carry @vch.ca email addresses. That is Vancouver
--     Coastal Health — the hospital CLIENT, not Graham — almost certainly derived from
--     the Richmond Hospital work. Graham's verified pattern is firstname.lastname@graham.ca.
--     The wrong addresses are cleared rather than replaced with a guess.
--
-- And extended with what the dossier research turned up: the MCMP bridge, three Gracorp
-- hires out of KOR's own client base, and the live BC pipeline as of today.
--
-- ⚠ NOT DONE HERE, deliberately: Gracorp occupies TWO canonical rows — 77362
--   "Gracorp (Graham Group)" (3 people, 5 projects) and 25785 "Gracorp Properties LP"
--   (0 people, 3 projects). That is one real-world company in two rows, the exact defect
--   the module CLAUDE.md opens with. It is NOT merged here: a merge DELETES the loser and
--   belongs to tools/BdCanonicalDedup with its four gates, not to a hand-written
--   migration. Logged below so it is not re-discovered a third time.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @now datetimeoffset = sysdatetimeoffset();
DECLARE @prov nvarchar(200) = N'Graham-Gracorp-Dossier-2026-10-01';
DECLARE @graham bigint = 69232;   -- Graham Construction (GC)
DECLARE @gracorp bigint = 77362;  -- Gracorp (Graham Group)

SELECT 'BEFORE' AS Section,
       (SELECT COUNT(*) FROM opportunities.IntelSignal   WHERE CanonicalOrgId IN (69232,77362,25785) AND RetiredAtUtc IS NULL) AS Signals,
       (SELECT COUNT(*) FROM opportunities.IntelWork     WHERE CanonicalOrgId IN (69232,77362,25785) AND RetiredAtUtc IS NULL) AS Works,
       (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation WHERE CanonicalOrgId IN (69232,77362,25785) AND RetiredAtUtc IS NULL) AS Affiliations;

-- ---------------------------------------------------------------------------
-- 0. Provenance rows. Everything written below hangs off one of these.
-- ---------------------------------------------------------------------------
INSERT INTO opportunities.CanonicalOrgEnrichment
    (CanonicalOrgId, ProviderName, Status, Attempts, Notes, CreatedAtUtc, UpdatedAtUtc, LastRefreshAtUtc)
SELECT o.Id, @prov, N'Succeeded', 1,
       N'Dossier research for Conor Murtagh, Graham partner appreciation 2026-10-01. Deltek (Clendor/PR/ClendorProjectAssoc/LD/LedgerAR) + Apollo + named public sources.',
       @now, @now, @now
FROM (VALUES (69232),(77362),(25785)) AS o(Id)
WHERE NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                  WHERE e.CanonicalOrgId = o.Id AND e.ProviderName = @prov);

DECLARE @eGraham  bigint = (SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = 69232 AND ProviderName = @prov);
DECLARE @eGracorp bigint = (SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = 77362 AND ProviderName = @prov);
DECLARE @eGracLP  bigint = (SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = 25785 AND ProviderName = @prov);

-- ---------------------------------------------------------------------------
-- 1. Retire the two records that say the Richmond structural seat may be open.
-- ---------------------------------------------------------------------------
UPDATE opportunities.IntelSignal
SET RetiredAtUtc = @now,
    -- ⚠ RetiredReason is nvarchar(200). A longer string does not error the script, it
    --   terminates THAT STATEMENT ONLY and the batch carries on — which is how a first
    --   run of this migration left the retire undone while everything after it landed.
    RetiredReason = N'WRONG - Yurkovich-class error: structural on Richmond Yurkovich Pavilion is ENTUITIVE (ReNew Canada, 2026-10-01). Not an open seat, a locked alliance. Superseded by migration 330.',
    UpdatedAtUtc = @now
WHERE CanonicalOrgId = @graham
  AND RetiredAtUtc IS NULL
  AND Subject LIKE N'%Richmond Hospital%SE slot may be open%';

UPDATE opportunities.IntelSignal
SET RetiredAtUtc = @now,
    RetiredReason = N'WRONG - asserted HDR might carry structural in-house on Richmond because the SE was "unknown". The engineer is Entuitive and is public. Retired by migration 330.',
    UpdatedAtUtc = @now
WHERE CanonicalOrgId = @graham
  AND RetiredAtUtc IS NULL
  AND Subject LIKE N'HDR (in-house engineering)%';

-- The "Unknown BC SE firms — OPEN (3 projects)" signal is kept, because Douglas College,
-- Cameron Community Centre and Stuart Lake are NOT all alliance jobs — but its claim is
-- rewritten so it can never again be read as "the seat is open".
UPDATE opportunities.IntelSignal
SET Subject = N'BC SE sub-consultants not publicly named on three Graham projects — UNKNOWN, not open',
    Detail  = N'Douglas College, Cameron Community Centre and Stuart Lake Hospital used project-specific SE sub-consultants not identified in public sources. ⚠ READ THIS CORRECTLY: not-named means NOT KNOWN, never "available". Where the delivery model is progressive design-build or alliance, structural is locked by the design-builder at team selection and is not re-tendered — Richmond Hospital on this same client proves it, where the engineer is Entuitive. Before treating any of these as a target, establish the delivery model and whether the team is already selected.',
    SourceProviderName = @prov,
    SourceEnrichmentId = @eGraham,
    LastSeenAtUtc = @now,
    UpdatedAtUtc = @now
WHERE CanonicalOrgId = @graham
  AND RetiredAtUtc IS NULL
  AND Subject LIKE N'Unknown BC SE firms%';

-- ---------------------------------------------------------------------------
-- 2. Clear the two wrong email addresses. No guess substituted.
-- ---------------------------------------------------------------------------
UPDATE p
SET p.Email = NULL,
    p.EmailSource = N'cleared-2026-10-01',
    p.EmailConfidence = NULL,
    p.EmailCheckedAtUtc = @now,
    p.Notes = LEFT(ISNULL(p.Notes + N' ', N'')
            + N'[2026-10-01] Held a @vch.ca address — Vancouver Coastal Health, the hospital client, not Graham. Bad derivation from the Richmond Hospital work; cleared rather than guessed. Graham''s verified pattern is firstname.lastname@graham.ca (confirmed on tim.heavenor@ and nancy.chadwick@). Switchboard 604.940.4500.', 4000),
    p.UpdatedAtUtc = @now
FROM opportunities.IntelPerson p
JOIN opportunities.IntelPersonAffiliation a ON a.IntelPersonId = p.Id AND a.CanonicalOrgId = @graham
WHERE p.RetiredAtUtc IS NULL AND ISNULL(p.Email, N'') LIKE N'%@vch.ca';

-- ---------------------------------------------------------------------------
-- 3. Rewrite the two Graham narratives that are wrong or now misleading.
-- ---------------------------------------------------------------------------
UPDATE opportunities.IntelNarrative
SET ParagraphText = N'Graham began in 1926 as a small family contractor on the Saskatchewan railway and is CELEBRATING ITS CENTENNIAL IN 2026 (theme "Defining a Century"). ⚠ It is NOT family-owned and has not been for forty years: it restructured in the early-1980s downturn and offered employees shares, and is today 100% EMPLOYEE-OWNED — more than 3,000 people, 29 permanent offices in Canada and the US, a project backlog approaching $6B, and CAD $4.3B of 2024 revenue, among Canada''s three or four largest contractors. President and CEO is Andy Trewick. Canada''s Best Managed Companies for a 26th consecutive year; a Top 100 Employer for 2026. Its BC buildings work is healthcare and institutional, delivered by progressive design-build and competitive alliance — in which the design-builder assembles architect and engineering sub-consultants, STRUCTURAL INCLUDED, at team selection. Richmond Hospital (Entuitive) is the worked example. BC leadership: Lee Holland (VP, Buildings — BC), Jordan Hood (District Manager, Buildings — BC), Tyler Johnston-Watson (District Manager, Infrastructure & Industrial — BC). Graham BC and Gracorp share Suite 700, 700 West Pender St, Vancouver; switchboard 604.940.4500.',
    SourceProviderName = @prov,
    SourceEnrichmentId = @eGraham,
    LastSeenAtUtc = @now,
    UpdatedAtUtc = @now
WHERE CanonicalOrgId = @graham AND NarrativeType = N'History' AND RetiredAtUtc IS NULL;

UPDATE opportunities.IntelNarrative
SET ParagraphText = N'⚠ SUPERSEDES the June 2026 action, which told us to ask Graham whether the Richmond Hospital structural seat was open. IT IS NOT — Entuitive is the engineer. Do not ask for it. THE ROUTE IN IS GRACORP, NOT THE CONTRACTOR. Graham the GC procures structural inside alliance and progressive design-build teams, where the seat is filled at team selection and KOR has no credential to displace the incumbent; Gracorp the developer procures its own consultants per project on market rental high-rise, which is KOR''s demonstrated lane and is won on relationship. Two warm threads exist and neither runs through Graham: (1) MUSSON CATTELL MACKEY is the architect on both Gracorp''s 1470 W Broadway tower and the Edmonds Community Hub, and MCMP has been the architect on seven KOR projects worth $3,033,046 billed, the most recent invoiced 2026-09-03 (Bridge & Elliot, Delta) — so the relationship is live, not historic; (2) three of Gracorp''s six Vancouver development staff came out of KOR''s own client base — Adrien Rahbar from Anthem, Sean O''Flynn from Wesgroup (and Amacon, and Beedie), Josh Guy from Bosa (and Starlight) — all of which KOR still invoices. On the contractor side the only legitimate ask is to be added to the pre-approved structural sub list ahead of a future design-build procurement; that is a list question, never a live-alliance question.',
    SourceProviderName = @prov,
    SourceEnrichmentId = @eGraham,
    LastSeenAtUtc = @now,
    UpdatedAtUtc = @now
WHERE CanonicalOrgId = @graham AND NarrativeType = N'Action' AND RetiredAtUtc IS NULL;
GO

-- ---------------------------------------------------------------------------
-- 4. New signals. Deterministic SHA1 natural keys, matching the existing convention.
-- ---------------------------------------------------------------------------
DECLARE @now datetimeoffset = sysdatetimeoffset();
DECLARE @prov nvarchar(200) = N'Graham-Gracorp-Dossier-2026-10-01';
DECLARE @eGraham  bigint = (SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = 69232 AND ProviderName = @prov);
DECLARE @eGracorp bigint = (SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = 77362 AND ProviderName = @prov);

-- OccurredAtApprox is nvarchar(100) holding 'YYYY-MM', not a date — match the convention
-- already in the table rather than inventing a second one.
DECLARE @sig TABLE (OrgId bigint, Enr bigint, SignalType nvarchar(100), Subject nvarchar(1000),
                    Detail nvarchar(max), Occurred nvarchar(100), Url nvarchar(2000));

INSERT INTO @sig VALUES
 (69232, @eGraham, N'CORRECTION',
  N'Richmond Hospital Yurkovich Family Pavilion structural is ENTUITIVE — the seat is closed',
  N'ReNew Canada''s project record for the $1.96B Richmond Hospital Redevelopment names the Engineer as Entuitive, the architect as HDR Architecture, Graham Design Builders LP as Phase 2 alliance partner and Turner & Townsend as cost consultant. Four phases 2022-2031, currently Phase One (Park Centre and Rotunda decant and demolition), completion expected 2031. This supersedes and retires the 2025-02 signal that said the SE slot "may be open" and told us to phone Graham immediately. DO NOT ASK FOR THIS SEAT.',
  N'2026-10', N'https://www.renewcanada.net/the-projects/richmond-hospital-redevelopment/'),

 (69232, @eGraham, N'MILESTONE',
  N'Graham centennial 2026 — 100 years, 100% employee-owned',
  N'Founded 1926 on the Saskatchewan railway; employee share ownership from the early 1980s; now fully employee-owned, 3,000+ staff, 29 offices, backlog approaching $6B, CAD $4.3B 2024 revenue. Centennial theme "Defining a Century"; cornerstone initiative is Graham''s Monumental Ride for youth mental health. The employee ownership is the thing to acknowledge — very few contractors their size are.',
  N'2026-02', N'https://www.grahambuilds.com/news/graham-marks-100-years-of-building-what-matters/'),

 (69232, @eGraham, N'RecentWin',
  N'Two Silver Awards at the VRCA 2026 Awards of Excellence',
  N'Graham took two Silver Awards at the Vancouver Regional Construction Association 2026 Awards of Excellence on 2026-09-28, for the Vancouver Transit Centre Skybridge Rehabilitation and the OMD Tank Farm Project. Local, current and theirs — the most recent thing to congratulate a Graham BC person on.',
  N'2026-09', NULL),

 (69232, @eGraham, N'KOR_RELATIONSHIP',
  N'KOR has ZERO Deltek history with Graham — an active client record with no work behind it',
  N'Checked 2026-10-01 against Deltek: Graham Construction is ClientID 2399d6aef0e14476a33c71e2f70683ad, ClientInd=Y, Status A — so it autocompletes and looks like a relationship. It is not one. 0 of 37,036 PR records are billed to it, 0 of 14,985 ClendorProjectAssoc rows reference it, and it carries 0 contacts. Gracorp has no Deltek record at all. Never imply shared history with Graham; every warm thread runs through somebody else.',
  N'2026-10', NULL),

 (77362, @eGracorp, N'KOR_OPPORTUNITY',
  N'⭐ THE BRIDGE: Musson Cattell Mackey is the architect on two Gracorp BC projects and on seven KOR jobs',
  N'MCMP (Musson Cattell Mackey Partnership) is the architect on BOTH Gracorp''s 1470-1476 W Broadway / South Granville Station tower AND the Edmonds Community Hub in Burnaby. MCMP is Deltek client CL00493 and has been the architect of record on seven KOR projects totalling $3,033,046 billed: Hudson Place One ($1,170,466), Luxe No. 3 Rd Richmond ($835,162), Park Royal Cinemas ($410,653), 1111-1113 Kingsway & Glen ($297,998), Bridge & Elliot Delta ($199,500, last invoiced 2026-09-03 — LIVE), W2ND 336-302 West 2nd ($104,774) and 1151 West Georgia ($14,493). ⚠ ALL SEVEN were invoiced to the DEVELOPER, never to MCMP — say "we have been the structural engineer on seven projects with MCMP", never "MCMP is our client".',
  N'2026-10', NULL),

 (77362, @eGracorp, N'KOR_OPPORTUNITY',
  N'⭐ Three of Gracorp''s six Vancouver development staff came out of KOR''s client base',
  N'Verified via Apollo employment history 2026-10-01 and KOR''s Deltek ledger. Adrien Rahbar (VP Real Estate, Vancouver) was at ANTHEM PROPERTIES May 2022 - May 2023; KOR holds 31 Anthem projects, $4,357,381 billed, last invoiced 2026-09-29. Sean O''Flynn (Senior Development Manager, joined Oct 2025) came from WESGROUP (2023-25), AMACON (2021-23) and BEEDIE (2019-21); KOR holds Wesgroup 54 projects / $7,368,326 (last invoiced 2026-09-30), Amacon 5 / $616,964, Beedie 22 / $1,563,744. Josh Guy (Senior Construction Manager, joined Dec 2024) came from BOSA PROPERTIES (2021-24) and STARLIGHT (2018-20); KOR holds Bosa 78 projects / $3,112,745 and Starlight 11 / $126,525. Also note Jennifer Scott (Manager, Major Project Development) trained as a STRUCTURAL EIT at Wicke Herfst Maver. Rahbar additionally chairs the City of North Vancouver Advisory Planning Commission and was Vice-Chair of Vancouver''s Urban Design Panel.',
  N'2026-10', NULL),

 (77362, @eGracorp, N'RecentWin',
  N'Edmonds Community Hub groundbreaking, Burnaby — 18 September 2026',
  N'Gracorp broke ground on the Edmonds Community Hub on 2026-09-18: a 50-storey tower with 480 purpose-built rental homes (384 market + 96 below-market) plus a community hub building containing an auditorium, food bank and arts facilities. Delivered with BC Builds (a $90M grant) and The Neighbourhood Church. Architect MCMP, drawing on the 1912 Edmonds Baptist Church. Our file values the project at $330M.',
  N'2026-09', N'https://renx.ca/gracorp-bc-builds-break-ground-on-480-unit-burnaby-rental-tower'),

 (77362, @eGracorp, N'RISK',
  N'⛔ 1045 Burnaby Street is in litigation with Fiera Real Estate — do not raise it',
  N'Gracorp Properties sued Fiera Real Estate in the Supreme Court of British Columbia in August 2024 over the 16-storey, 170-unit West End rental project at 1045 Burnaby St (site bought for $28.83M in 2021). Fiera held 90%, Gracorp 10% as development and construction manager. Gracorp granted a neighbour a $200,000 crane-swing easement in April 2024; Fiera issued default notices in June, terminated both agreements in July and invoked a buyout of Gracorp''s stake at 90% of fair market value, closing expected 2024-11-07. Gracorp alleges there was no valid basis to terminate. OUTCOME NOT VERIFIED. Treat the address as off-limits in conversation.',
  N'2024-08', N'https://storeys.com/gracorp-fiera-1045-burnaby-vancouver-lawsuit/'),

 (77362, @eGracorp, N'DATA_QUALITY',
  N'⚠ Gracorp occupies two canonical rows — 77362 and 25785 — route to BdCanonicalDedup',
  N'Canonical 77362 "Gracorp (Graham Group)" (3 affiliations, 5 IntelWork rows) and canonical 25785 "Gracorp Properties LP" (0 affiliations, 3 IntelWork rows) are one real-world company held twice, so any read keyed on one misses the other''s projects and people. NOT merged by migration 330 on purpose: a merge DELETES the loser row and belongs to tools/BdCanonicalDedup with its four gates (name similarity, both-carry-a-Deltek-id, branch-row direction, country assertion). Neither row carries a Deltek id, so the hard gate does not block it. Logged so this is not rediscovered a third time.',
  N'2026-10', NULL);

INSERT INTO opportunities.IntelSignal
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
     CanonicalOrgId, SignalType, Subject, Detail, OccurredAtApprox, SourceUrl, Corroborations)
SELECT @prov, s.Enr, N'High',
       CONVERT(varchar(40), HASHBYTES('SHA1', CONVERT(varchar(900), @prov + N'|' + CONVERT(nvarchar(20), s.OrgId) + N'|' + s.Subject)), 2),
       @now, @now, @now, @now,
       s.OrgId, s.SignalType, s.Subject, s.Detail, s.Occurred, s.Url, 1
FROM @sig s
WHERE NOT EXISTS (SELECT 1 FROM opportunities.IntelSignal x
                  WHERE x.CanonicalOrgId = s.OrgId AND x.Subject = s.Subject AND x.RetiredAtUtc IS NULL);

SELECT 'signals inserted' AS Section, @@ROWCOUNT AS Rows;
GO

-- ---------------------------------------------------------------------------
-- 5. Two BC projects missing from the pipeline.
-- ---------------------------------------------------------------------------
DECLARE @now datetimeoffset = sysdatetimeoffset();
DECLARE @prov nvarchar(200) = N'Graham-Gracorp-Dossier-2026-10-01';
DECLARE @eGracorp bigint = (SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = 77362 AND ProviderName = @prov);

-- YearApprox is nvarchar(40) in this table, not an int.
DECLARE @work TABLE (ProjectName nvarchar(1000), YearApprox nvarchar(40), ValueText nvarchar(200), Notes nvarchar(max));
INSERT INTO @work VALUES
 (N'Kits West (Kitsilano, Vancouver)', N'2026', N'20 storeys, 182 units',
  N'A SECOND Kitsilano rental tower alongside Kits East (2175 W 7th Ave, 20 storeys, 183 units of which 35 below-market). Reported September 2026; was absent from our file entirely.'),
 (N'1045 Burnaby Street (West End, Vancouver)', N'2024', N'16 storeys, 170 units (37 below-market)',
  N'⛔ IN LITIGATION with JV partner Fiera Real Estate — see the RISK signal. Do not raise in conversation. Recorded for completeness of the pipeline only.');

INSERT INTO opportunities.IntelWork
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
     CanonicalOrgId, ProjectName, NormalizedProjectName, Role, YearApprox, EstimatedValueText, Notes)
SELECT @prov, @eGracorp, N'High',
       CONVERT(varchar(40), HASHBYTES('SHA1', CONVERT(varchar(900), @prov + N'|77362|' + w.ProjectName)), 2),
       @now, @now, @now, @now,
       77362, w.ProjectName,
       LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(w.ProjectName, N' ', N''), N',', N''), N'&', N''), N'.', N''), N'(', N''), N')', N'')),
       N'Developer', w.YearApprox, w.ValueText, w.Notes
FROM @work w
WHERE NOT EXISTS (SELECT 1 FROM opportunities.IntelWork x
                  WHERE x.CanonicalOrgId = 77362 AND x.ProjectName = w.ProjectName AND x.RetiredAtUtc IS NULL);

SELECT 'works inserted' AS Section, @@ROWCOUNT AS Rows;
GO

-- ---------------------------------------------------------------------------
-- 6. The Gracorp people we did not hold. Verified work emails only — the natural key
--    is SHA1 of the lowered email over VARCHAR, so a person without one cannot be
--    keyed this way and is deliberately omitted (Tyler Johnston-Watson, Graham BC).
-- ---------------------------------------------------------------------------
DECLARE @now datetimeoffset = sysdatetimeoffset();
DECLARE @prov nvarchar(200) = N'Graham-Gracorp-Dossier-2026-10-01';
DECLARE @eGracorp bigint = (SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = 77362 AND ProviderName = @prov);

-- EmailSource is a short token ('Apollo', 'Hunter', 'PatternInferred') and EmailConfidence
-- is a tinyint 0-100, not a word — matching what the 14,833 existing rows already use.
DECLARE @ppl TABLE (FullName nvarchar(400), Email varchar(400), Title nvarchar(400),
                    LinkedIn nvarchar(1000), EmailSource nvarchar(40), EmailConf tinyint,
                    Notes nvarchar(max));
INSERT INTO @ppl VALUES
 (N'Sean O''Flynn', 'sean.oflynn@gracorp.com', N'Senior Development Manager',
  N'http://www.linkedin.com/in/seantoflynn', N'Apollo', 85,
  N'Vancouver. Joined Gracorp October 2025. Previously Wesgroup Properties (Nov 2023 - Jun 2025), Amacon (2021-23), Beedie Living (2019-21) — all three are current KOR clients. Conor Murtagh has 548 hours on Wesgroup''s ACE at 1650 E 12th spanning O''Flynn''s entire Wesgroup tenure, and 174.5 hours on Amacon''s Vue at 500 Foster Ave.'),
 (N'Josh Guy', 'josh.guy@gracorp.com', N'Senior Construction Manager',
  N'http://www.linkedin.com/in/josh-guy-61981387', N'Apollo', 85,
  N'Vancouver. Joined Gracorp December 2024. Previously Bosa Properties (2021-24) and Starlight Investments (2018-20), both KOR clients. ⚠ KOR''s Bosa work that Conor touched (1002-1012 Pandora Ave) ran 2016-2019, BEFORE Guy''s time — same client, different era, do not imply overlap.'),
 (N'Derek Steven', 'derek.steven@gracorp.com', N'Vice President, Capital Markets',
  N'http://www.linkedin.com/in/derek-steven-9464424', N'Apollo', 85,
  N'Vancouver. At Gracorp since Nov 2020, VP since 2023. Previously BMO Financial Group 2010-2020 (Director, Corporate Finance). The money side rather than the design side.'),
 (N'Jennifer Scott', 'jscott@gracorpcapital.com', N'Manager, Major Project Development',
  N'http://www.linkedin.com/in/jennifer-scott-83907428', N'Apollo', 85,
  N'⭐ Vancouver. TRAINED AS A STRUCTURAL ENGINEER — Structural Engineer EIT at Wicke Herfst Maver Consulting 2008-09, and civil EIT at Klohn Crippen Berger before that; then Bilfinger Berger Project Investments (P3 development) 2009-2012. At Gracorp Capital since 2012. The one person on the Gracorp side who will follow a structural argument without translation.'),
 (N'Giuseppe Augello', 'giuseppe.augello@gracorp.com', N'Vice President, Real Estate',
  N'http://www.linkedin.com/in/giuseppe-augello-ccim-57173b2', N'PatternInferred', 55,
  N'⚠ SEATTLE, not Vancouver — runs the Washington State side; was Managing Director, Washington before 2023. Email is DERIVED from the verified gracorp.com firstname.lastname pattern, NOT verified by Apollo, which had no address for him. Treat as unconfirmed.'),
 (N'Nate Hickey', 'nate.hickey@gracorp.com', N'Senior Development Manager',
  N'http://www.linkedin.com/in/natehickey', N'Apollo', 85,
  N'⚠ SEATTLE, not Vancouver. At Gracorp since 2021. Multifamily and mixed-use, 500+ units.');

MERGE opportunities.IntelPerson AS t
USING (SELECT FullName, Email, LinkedIn, Notes, EmailSource, EmailConf,
              CONVERT(varchar(40), HASHBYTES('SHA1', CONVERT(varchar(400), LOWER(Email))), 2) AS NK
       FROM @ppl) AS s
ON t.NaturalKey = s.NK
WHEN NOT MATCHED THEN
  INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
          FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
          DisplayName, NormalizedName, Email, LinkedinUrl, Notes, Corroborations,
          EmailSource, EmailConfidence, EmailCheckedAtUtc)
  VALUES (@prov, @eGracorp, N'High', s.NK,
          @now, @now, @now, @now,
          s.FullName,
          LOWER(REPLACE(REPLACE(REPLACE(s.FullName, N' ', N''), N'''', N''), N'.', N'')),
          s.Email, s.LinkedIn, s.Notes, 1,
          s.EmailSource, s.EmailConf, @now);

SELECT 'people upserted' AS Section, @@ROWCOUNT AS Rows;

INSERT INTO opportunities.IntelPersonAffiliation
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
     IntelPersonId, CanonicalOrgId, Title, IsCurrent, Notes)
SELECT @prov, @eGracorp, N'High',
       CONVERT(varchar(40), HASHBYTES('SHA1', CONVERT(varchar(900),
            CONVERT(varchar(20), p.Id) + '|77362|' + LOWER(LTRIM(RTRIM(CONVERT(varchar(400), s.Title)))))), 2),
       @now, @now, @now, @now,
       p.Id, 77362, s.Title, 1, s.Notes
FROM (SELECT FullName, Title, Notes,
             CONVERT(varchar(40), HASHBYTES('SHA1', CONVERT(varchar(400), LOWER(Email))), 2) AS NK
      FROM @ppl) s
JOIN opportunities.IntelPerson p ON p.NaturalKey = s.NK
WHERE NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a
                  WHERE a.IntelPersonId = p.Id AND a.CanonicalOrgId = 77362 AND a.RetiredAtUtc IS NULL);

SELECT 'affiliations inserted' AS Section, @@ROWCOUNT AS Rows;

-- The two Gracorp people we already held carried no email at all. Apollo verified both
-- on 2026-10-01; fill only where the column is empty, so a better address is never
-- overwritten by this migration.
UPDATE p
SET p.Email = v.Email,
    p.EmailSource = N'Apollo',
    p.EmailConfidence = 85,
    p.EmailCheckedAtUtc = @now,
    p.UpdatedAtUtc = @now
FROM opportunities.IntelPerson p
JOIN (VALUES (N'Adrien Rahbar', N'adrien.rahbar@gracorp.com'),
             (N'Bruce Black',   N'bruce.black@gracorp.com')) AS v(Nm, Email)
  ON p.DisplayName = v.Nm
JOIN opportunities.IntelPersonAffiliation a
  ON a.IntelPersonId = p.Id AND a.CanonicalOrgId = 77362 AND a.RetiredAtUtc IS NULL
WHERE p.RetiredAtUtc IS NULL AND ISNULL(p.Email, N'') = N'';

SELECT 'existing Gracorp emails filled' AS Section, @@ROWCOUNT AS Rows;
GO

-- ---------------------------------------------------------------------------
-- 7. After.
-- ---------------------------------------------------------------------------
SELECT 'AFTER' AS Section,
       (SELECT COUNT(*) FROM opportunities.IntelSignal   WHERE CanonicalOrgId IN (69232,77362,25785) AND RetiredAtUtc IS NULL) AS Signals,
       (SELECT COUNT(*) FROM opportunities.IntelWork     WHERE CanonicalOrgId IN (69232,77362,25785) AND RetiredAtUtc IS NULL) AS Works,
       (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation WHERE CanonicalOrgId IN (69232,77362,25785) AND RetiredAtUtc IS NULL) AS Affiliations;

SELECT 'the Richmond claim is gone' AS Check_,
       COUNT(*) AS StillSayingTheSeatMayBeOpen
FROM opportunities.IntelSignal
WHERE RetiredAtUtc IS NULL AND CanonicalOrgId IN (69232,77362,25785)
  AND (Subject LIKE N'%slot may be open%' OR Detail LIKE N'%confirm SE allocation%');

SELECT 'no @vch.ca addresses remain on Graham' AS Check_, COUNT(*) AS Rows
FROM opportunities.IntelPerson p
JOIN opportunities.IntelPersonAffiliation a ON a.IntelPersonId = p.Id AND a.CanonicalOrgId = 69232
WHERE p.RetiredAtUtc IS NULL AND ISNULL(p.Email, N'') LIKE N'%@vch.ca';

SELECT 'Gracorp roster now' AS Section, p.DisplayName, a.Title, p.Email
FROM opportunities.IntelPersonAffiliation a
JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
WHERE a.CanonicalOrgId IN (77362, 25785) AND a.RetiredAtUtc IS NULL
ORDER BY p.DisplayName;
GO
