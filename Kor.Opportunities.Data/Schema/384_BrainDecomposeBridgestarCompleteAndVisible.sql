/*
    KorOpportunitiesDb 384: finishes the decompose pass for Conor's brief. Migration 382 stopped short:

    - It wrote OrgFact, which no application screen reads (only the parked /ask prompt names it). This migration also writes
      IntelNarrative, IntelWork and IntelSignal, which IntelReadService shows on the org's screen.
    - It wrote no relationship edge and no touchpoint. Graham Chatwin to Conor Murtagh and Jeremy Atkinson are written here.
    - Henrik Drago's person key used name|org. ComputePerson uses LinkedIn before name when there is no email. Corrected.
    - Past employers that make the routes in (Mission, Stober) are recorded as past affiliations.
    - People on public record but not banked: Santan Singh, Logan Simpson, Kieran Trimbee, Richard Boerger, Sherri Turpin.
    - Org 68987's Action paragraph said "No KOR BD priority" and is replaced.

    No CRM pursuit is opened: that follows when Conor takes this on (board row 1634). Touchpoints sit on Mission Group, where the
    contact happened, so Bridgestar's warmth is not overstated.

    APPLIED 4 Nov 2026 (its rows read back live on 5 Nov, provider Conor-Okanagan-Brief-2026-11-04). The statements below are what ran, so
    they are left as they ran; this header was corrected on 5 Nov after review row 511. The live rows still carry the first six faults,
    because correcting them needs a data change. Board row 1644 retires these hand rows through the pipeline:

    - NOT IDEMPOTENT. Do not re-run. The enrichment MERGE is ON 1 = 0, and UX_CanonicalOrgEnrichment_OrgProvider (unique, live on 5 Nov)
      throws on a second run, which rolls the whole migration back. Only the people, edge, touchpoint, narrative, work and signal writes are keyed.
    - Narrative, work and signal keys are private strings, and works hash NVARCHAR, so they do not match IntelPersistenceService. Until row
      1644 runs, a pipeline run on these orgs writes second rows beside them. Do not run research on these orgs first.
    - Work names strip 5 characters where IntelNaturalKey.Normalize strips 10, so the Springfield Rd row keeps '(', '/' and ')'.
    - All 3 signals are typed Other. Graham Chatwin's and the senior-team signal are leadership changes, so RecentLeadershipChanges
      (IntelReadService, SignalType = LeadershipChange) does not show them.
    - Conor's and Jeremy's emails carry no EmailSource or EmailConfidence (read 5 Nov: NULL on 1085 and 1088).
    - Three narrative sentences state a search's miss as a fact. "not on public record" (1838121, 1838122) means not found on any readable
      public record (382), and row 1632 says Kelowna's records refuse automated reads. "KOR has no history with this firm" (68987) means no
      Deltek client under the name (382) and no filed email (383's 5 Nov search); mail never filed is not covered.

    Checked and found sound on 5 Nov: the retire of 68987's old Action paragraph has no row-count check, but it landed (narrative 6791
    retired, 18586 live). Org 54726 had no earlier narrative: its 2 are both from here.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

DECLARE @provider NVARCHAR(120) = N'Conor-Okanagan-Brief-2026-11-04';
DECLARE @by NVARCHAR(100) = N'BrainDecompose-2026-11-04';
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

DECLARE @graham BIGINT = (SELECT Id FROM opportunities.IntelPerson WHERE Email = N'gchatwin@bridgestar.ca');
DECLARE @melissa BIGINT = (SELECT Id FROM opportunities.IntelPerson WHERE Email = N'mbell@bridgestar.ca');
DECLARE @scott BIGINT = (SELECT Id FROM opportunities.IntelPerson WHERE Email = N'swandler@bridgestar.ca');
IF @graham IS NULL OR @melissa IS NULL OR @scott IS NULL THROW 50001, N'384: a person from 382 is missing', 1;

/* KOR staff: the existing nodes get their work email, so an edge has a real person at each end */
UPDATE opportunities.IntelPerson SET Email = COALESCE(Email, N'cmurtagh@korstructural.com'), UpdatedAtUtc = @now WHERE Id = 1088 AND DisplayName = N'Conor Murtagh';
IF @@ROWCOUNT <> 1 THROW 50002, N'384: Conor row 1088 not as expected', 1;
UPDATE opportunities.IntelPerson SET Email = COALESCE(Email, N'jatkinson@korstructural.com'), UpdatedAtUtc = @now WHERE Id = 1085 AND DisplayName = N'Jeremy Atkinson';
IF @@ROWCOUNT <> 1 THROW 50003, N'384: Jeremy row 1085 not as expected', 1;

/* Henrik Drago: the contract's key */
UPDATE opportunities.IntelPerson
   SET NaturalKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(LOWER(N'http://www.linkedin.com/in/henrik-drago-94073a292') AS VARCHAR(8000))), 2), UpdatedAtUtc = @now
 WHERE DisplayName = N'Henrik Drago' AND Email IS NULL AND SourceProviderName = N'Apollo-Verified-2026-11-04';
IF @@ROWCOUNT <> 1 THROW 50004, N'384: Henrik Drago row not as expected', 1;

DECLARE @orgs TABLE (OrgId BIGINT);
INSERT @orgs VALUES (1838121),(1838122),(1838123),(54726),(70567),(68987),(193),(70564);
DECLARE @enr TABLE (EnrId BIGINT, OrgId BIGINT);
MERGE opportunities.CanonicalOrgEnrichment AS T
USING @orgs AS S ON 1 = 0
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, LastRefreshAtUtc, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @provider, N'ok', 1, @now, @now, @now)
OUTPUT inserted.Id, inserted.CanonicalOrgId INTO @enr (EnrId, OrgId);

/* More people, current affiliations; past affiliations for the routes in */
DECLARE @c TABLE (OrgId BIGINT, Person NVARCHAR(200), Title NVARCHAR(200), Email NVARCHAR(200), PersonId BIGINT, IsCurrent BIT, Notes NVARCHAR(400), NormName NVARCHAR(200), NormTitle NVARCHAR(200), PKey CHAR(40), AffKey CHAR(40));
INSERT @c (OrgId, Person, Title, Email, PersonId, IsCurrent, Notes) VALUES
 (54726, N'Santan Singh', N'Director of Development', NULL, NULL, 1, N'Clark''s Crossing Gazette, 22 Oct 2025'),
 (70567, N'Logan Simpson', N'Project Manager', NULL, NULL, 1, N'teamconstruction.ca/about'),
 (70567, N'Kieran Trimbee', N'Project Coordinator', NULL, NULL, 1, N'teamconstruction.ca/about'),
 (1838122, N'Richard Boerger', N'Contact on Front St permit drawings', N'richard.boerger@stretchconstruction.ca', NULL, 1, N'DP PL2023-9703 drawings, April 2024'),
 (1838123, N'Sherri Turpin', N'Architect, AIBC', N'turpinarchitect@outlook.com', NULL, 1, N'DP PL2023-9703 drawings, April 2024'),
 (193, N'Graham Chatwin', N'Preconstruction Manager', NULL, @graham, 0, N'At Mission Group May 2016 to Jul 2025; on KOR job 31101 Timmins'),
 (70564, N'Melissa Bell', N'Commercial Leasing Manager', NULL, @melissa, 0, N'Feb 2022 to Jun 2026 (Apollo)'),
 (70564, N'Scott Wandler', N'Director of Leasing', NULL, @scott, 0, N'Sep 2013 to Dec 2024 (Apollo)');

UPDATE @c SET
  NormName = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(Person))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+',''),
  NormTitle = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','');
UPDATE @c SET PKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(
    CASE WHEN Email IS NOT NULL THEN LOWER(LTRIM(RTRIM(Email))) ELSE NormName + '|org:' + CAST(OrgId AS VARCHAR(20)) END AS VARCHAR(8000))), 2)
 WHERE PersonId IS NULL;

MERGE opportunities.IntelPerson WITH (HOLDLOCK) AS T
USING (SELECT c.PKey, c.Person, c.NormName, c.Email, e.EnrId FROM @c c JOIN @enr e ON e.OrgId = c.OrgId WHERE c.PersonId IS NULL) AS S
   ON T.NaturalKey = S.PKey
WHEN MATCHED THEN UPDATE SET Email = COALESCE(T.Email, S.Email), LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, DisplayName, NormalizedName, Email, EmailSource, EmailConfidence)
   VALUES (@provider, S.EnrId, N'High', S.PKey, @now, @now, S.Person, S.NormName, S.Email, CASE WHEN S.Email IS NULL THEN NULL ELSE N'asis' END, CASE WHEN S.Email IS NULL THEN NULL ELSE 80 END);

UPDATE c SET c.PersonId = p.Id FROM @c c JOIN opportunities.IntelPerson p ON p.NaturalKey = c.PKey WHERE c.PersonId IS NULL;
UPDATE @c SET AffKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(CAST(PersonId AS VARCHAR(20)) + '|' + CAST(OrgId AS VARCHAR(20)) + '|' + NormTitle AS VARCHAR(8000))), 2);

MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T
USING (SELECT c.AffKey, c.PersonId, c.OrgId, c.Title, c.IsCurrent, c.Notes, e.EnrId FROM @c c JOIN @enr e ON e.OrgId = c.OrgId) AS S
   ON T.NaturalKey = S.AffKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent, Notes)
   VALUES (@provider, S.EnrId, N'High', S.AffKey, @now, @now, S.PersonId, S.OrgId, S.Title, S.IsCurrent, S.Notes);

/* Relationship edges */
INSERT INTO opportunities.IntelPersonRelation (NaturalKey, FromPersonId, ToPersonId, RelationType, Context, EvidencedAtUtc, SourceProviderName, SourceRef, CreatedAtUtc, UpdatedAtUtc)
SELECT CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2), v.f, v.t, N'WorkedWith', v.ctx, v.at, @by, N'KorEmailIndex project 31101-01, read 2026-11-04', @now, @now
FROM (VALUES
 ('graham|conor|workedwith', @graham, CAST(1088 AS BIGINT), N'Direct email on Mission Group''s Timmins job, Jan 2025, with Graham as Mission''s Preconstruction Manager. He wrote to Conor on 21 Jan 2025: "The structure looks very good from the construction perspective."', CAST('2025-01-21T16:36:00+00:00' AS DATETIMEOFFSET)),
 ('graham|jeremy|workedwith', @graham, CAST(1085 AS BIGINT), N'Graham supplied project documents to Jeremy Atkinson on the Penticton project for Mission Group from Oct 2023.', CAST('2023-10-13T17:25:00+00:00' AS DATETIMEOFFSET))
) AS v(k, f, t, ctx, at)
WHERE NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonRelation r WHERE r.NaturalKey = CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2));

/* Touchpoints: past events, on Mission Group where they happened */
INSERT INTO opportunities.CrmTouchpoint (NaturalKey, CanonicalOrgId, EngagementId, IntelPersonId, Kind, OccurredAtUtc, Summary, KorStaff, CreatedBy, CreatedAtUtc)
SELECT CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2), 193, NULL, @graham, N'Email', v.at, v.s, v.staff, @by, @now
FROM (VALUES
 ('mission|graham|2025-01-14', CAST('2025-01-14T17:44:00+00:00' AS DATETIMEOFFSET), N'Graham Chatwin sent Conor the Timmins lobby sketches for feasibility review.', N'Conor Murtagh'),
 ('mission|graham|2025-01-21', CAST('2025-01-21T16:36:00+00:00' AS DATETIMEOFFSET), N'Graham Chatwin on the Timmins structural addendum: "The structure looks very good from the construction perspective."', N'Conor Murtagh')
) AS v(k, at, s, staff)
WHERE NOT EXISTS (SELECT 1 FROM opportunities.CrmTouchpoint c WHERE c.NaturalKey = CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2));

/* What the app shows: narratives */
UPDATE opportunities.IntelNarrative SET RetiredAtUtc = @now, RetiredReason = N'Superseded by the Okanagan review of 4 Nov 2026', UpdatedAtUtc = @now
 WHERE CanonicalOrgId = 68987 AND NarrativeType = N'Action' AND ParagraphText LIKE N'No KOR BD priority%' AND RetiredAtUtc IS NULL;

INSERT INTO opportunities.IntelNarrative (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, CanonicalOrgId, NarrativeType, ParagraphText)
SELECT @provider, e.EnrId, N'High', CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2), @now, @now, @now, @now, v.org, v.nt, v.txt
FROM (VALUES
 ('n|1838121|current', CAST(1838121 AS BIGINT), N'Current', N'Bridgestar Group is a Kelowna owner, developer and property manager founded in 2020, led by President Gurupdesh Dhaliwal, about 6 staff. Four of its five senior people joined in 2026. Next building: Backstreet Apartments, 64 homes proposed at 32 Backstreet Blvd, Penticton. Uses outside contractors: Stretch Construction on 123 Front St and 681 Wardlaw, with Sherri Turpin Architect on Front St. Its structural engineer is not on public record.'),
 ('n|1838121|action', CAST(1838121 AS BIGINT), N'Action', N'Pursue. Conor Murtagh worked directly with Director of Construction Graham Chatwin on Mission Group''s Timmins job in Jan 2025. Ask where Backstreet stands, who is engineering it, and how consultants will be picked. Decision due 27 Nov 2026.'),
 ('n|1838122|current', CAST(1838122 AS BIGINT), N'Current', N'Stretch Construction is a Kelowna design-to-delivery commercial and multi-family contractor, about 7 staff, President Geoff Stretch. Contractor for Bridgestar on 123 Front St, Penticton and 681 Wardlaw, Kelowna. Who it uses for structural is not on public record and decides any Bridgestar pursuit.'),
 ('n|1838123|current', CAST(1838123 AS BIGINT), N'Current', N'Penticton architect. Architect on Bridgestar''s 123 Front St, working with Stretch Construction.'),
 ('n|54726|current', CAST(54726 AS BIGINT), N'Current', N'Ironclad Developments is a Winnipeg-based, vertically integrated apartment developer and builder founded in 2014, about 5,000 units across five provinces. It runs design and engineering in-house and lists Andrew Meads as Lead Structural Engineer. Who seals its BC buildings is not confirmed.'),
 ('n|54726|action', CAST(54726 AS BIGINT), N'Action', N'Treat the structural seat as closed until proven otherwise. One question to COO Janet Sokolowski: who seals BC structural? Stop if the answer is in-house.'),
 ('n|68987|action', CAST(68987 AS BIGINT), N'Action', N'A contractor, so a channel and not a client: the owner usually picks the engineer, except on design-build and development management. One coffee with President Chad Gould. KOR has no history with this firm. Registered as Team Construction Management (1981) Ltd.')
) AS v(k, org, nt, txt)
JOIN @enr e ON e.OrgId = v.org
WHERE NOT EXISTS (SELECT 1 FROM opportunities.IntelNarrative n WHERE n.NaturalKey = CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2));

/* What the app shows: works */
INSERT INTO opportunities.IntelWork (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, CanonicalOrgId, ProjectName, NormalizedProjectName, Role, Notes)
SELECT @provider, e.EnrId, N'High', CONVERT(varchar(40), HASHBYTES('SHA1', CONCAT(CAST(v.org AS varchar(20)), '|', v.pn, '|', v.role)), 2), @now, @now, @now, @now, v.org, v.pn,
       REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(v.pn),' ',''),',',''),'.',''),'-',''),'''',''), v.role, v.note
FROM (VALUES
 (CAST(1838121 AS BIGINT), N'Backstreet Apartments, 32 Backstreet Blvd, Penticton', N'Developer', N'64 homes proposed; planned'),
 (CAST(1838121 AS BIGINT), N'Nova on the Park, 681 Wardlaw Ave, Kelowna', N'Developer', N'45 rentals, 5 storeys; due winter 2026-27'),
 (CAST(1838121 AS BIGINT), N'Astra Downtown, 123 Front St, Penticton', N'Developer', N'48 homes plus retail, 5 storeys'),
 (CAST(1838121 AS BIGINT), N'Sutherland Apartments, 598 Sutherland Ave, Kelowna', N'Developer', N'16 units'),
 (CAST(1838121 AS BIGINT), N'Chelmsford Plaza, Chelmsford, Ontario', N'Owner', N'Retail plaza'),
 (CAST(1838122 AS BIGINT), N'123 Front St, Penticton', N'General contractor', N'48 homes plus 3 commercial units, for Bridgestar'),
 (CAST(1838122 AS BIGINT), N'681 Wardlaw Ave, Kelowna', N'General contractor', N'45 rentals, for Bridgestar'),
 (CAST(1838123 AS BIGINT), N'123 Front St, Penticton', N'Architect', N'For Bridgestar; DP PL2023-9703'),
 (CAST(54726 AS BIGINT), N'2241 Springfield Rd, Kelowna (Parkview Valley / Mission Heights)', N'Developer and builder', N'401 units, 6 storeys; sources conflict on status'),
 (CAST(54726 AS BIGINT), N'The Aurora, 230 Aurora Cres, Kelowna', N'Developer and builder', N'Listed complete on its site'),
 (CAST(54726 AS BIGINT), N'Lakeview Pointe, 3623 Elliot Rd, West Kelowna', N'Developer and builder', N'Listed complete on its site'),
 (CAST(54726 AS BIGINT), N'Rockwood Landing, 3400 Centennial Dr, Vernon', N'Developer and builder', N'Its pages conflict on status'),
 (CAST(54726 AS BIGINT), N'The Highland, 1803 31A St, Vernon', N'Developer and builder', N'Listed complete on its site')
) AS v(org, pn, role, note)
JOIN @enr e ON e.OrgId = v.org
WHERE NOT EXISTS (SELECT 1 FROM opportunities.IntelWork w WHERE w.NaturalKey = CONVERT(varchar(40), HASHBYTES('SHA1', CONCAT(CAST(v.org AS varchar(20)), '|', v.pn, '|', v.role)), 2));

/* What the app shows: signals */
INSERT INTO opportunities.IntelSignal (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc, CanonicalOrgId, SignalType, Subject, Detail, OccurredAtApprox, SourceUrl)
SELECT @provider, e.EnrId, N'High', CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2), @now, @now, @now, @now, v.org, N'Other', v.subj, v.det, v.at, v.url
FROM (VALUES
 ('s|1838121|graham', CAST(1838121 AS BIGINT), N'New Director of Construction: Graham Chatwin', N'Joined Oct 2026 from Sureway; Mission Group 2016 to 2025. Worked directly with KOR on Timmins.', N'2026-10', CAST(NULL AS NVARCHAR(400))),
 ('s|1838121|bench', CAST(1838121 AS BIGINT), N'Senior team built in 2026', N'CFO Scott Wandler and Technical PM Rashmeen Malhotra in March, Director of Operations Melissa Bell in June, Graham Chatwin in October.', N'2026', CAST(NULL AS NVARCHAR(400))),
 ('s|54726|fatal', CAST(54726 AS BIGINT), N'Worker death at 2241 Springfield Rd, Kelowna', N'14 Jun 2024. RCMP and WorkSafeBC investigated; no published outcome found as of 4 Nov 2026.', N'2024-06', N'https://globalnews.ca/news/10573360/kelowna-construction-site-death-investigation')
) AS v(k, org, subj, det, at, url)
JOIN @enr e ON e.OrgId = v.org
WHERE NOT EXISTS (SELECT 1 FROM opportunities.IntelSignal s WHERE s.NaturalKey = CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2));

/* Read back what the app's own queries would see */
SELECT o.Id, LEFT(o.DisplayName,30) AS Org,
 (SELECT COUNT(*) FROM opportunities.IntelNarrative x WHERE x.CanonicalOrgId=o.Id AND x.RetiredAtUtc IS NULL) AS Narratives,
 (SELECT COUNT(*) FROM opportunities.IntelSignal x WHERE x.CanonicalOrgId=o.Id AND x.RetiredAtUtc IS NULL) AS Signals,
 (SELECT COUNT(*) FROM opportunities.IntelWork x WHERE x.CanonicalOrgId=o.Id AND x.RetiredAtUtc IS NULL) AS Works,
 (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation x WHERE x.CanonicalOrgId=o.Id AND x.RetiredAtUtc IS NULL) AS People,
 (SELECT COUNT(*) FROM opportunities.OrgFact x WHERE x.CanonicalOrgId=o.Id AND x.RetiredAtUtc IS NULL) AS Facts,
 (SELECT COUNT(*) FROM opportunities.CrmTouchpoint x WHERE x.CanonicalOrgId=o.Id AND x.CreatedBy=@by) AS NewTouchpoints
FROM opportunities.CanonicalOrg o WHERE o.Id IN (1838121,1838122,1838123,54726,70567,68987,193,70564) ORDER BY o.Id;
SELECT r.RelationType, p1.DisplayName AS FromPerson, p2.DisplayName AS ToPerson, r.EvidencedAtUtc FROM opportunities.IntelPersonRelation r JOIN opportunities.IntelPerson p1 ON p1.Id=r.FromPersonId JOIN opportunities.IntelPerson p2 ON p2.Id=r.ToPersonId WHERE r.SourceProviderName=@by;
SELECT DisplayName, LEFT(NaturalKey,8) AS KeyStart FROM opportunities.IntelPerson WHERE DisplayName=N'Henrik Drago';

COMMIT;
