/*
    KorOpportunitiesDb 382: the decompose-to-brain pass for Conor's brief on Bridgestar, Team Construction and Ironclad
    (docs/KOR-Okanagan-Brief-Bridgestar-Team-Ironclad-2026-11-04-web.pdf).

    Orgs 1838121 Bridgestar Group, 1838122 Stretch Construction Ltd. and 1838123 Sherri Turpin Architect were created through
    tools/BdCanonicalDedup --create on 4 Nov 2026 so the resolver owns the fuzzy key.

    APPLIED 4 Nov 2026 (its rows read back live on 5 Nov, provider Apollo-Verified-2026-11-04). The statements below are what ran, so
    they are left as they ran; this header was corrected on 5 Nov after review row 509. Corrections, none of which ran:

    - NOT IDEMPOTENT. Do not re-run. The enrichment MERGE is ON 1 = 0, so it always inserts, and UX_CanonicalOrgEnrichment_OrgProvider
      (unique on org and provider, live on 5 Nov) throws on a second run, which rolls the whole migration back.
    - The real person key is email, then LinkedIn, then name|org (IntelNaturalKey.ComputePerson). This file skips LinkedIn, so Henrik
      Drago got a name|org key; migration 384 corrected his live row.
    - People are matched on NaturalKey only, not through usp_ResolveOrCreateIntelPerson. Read 5 Nov: of the 20 person rows carrying this
      or 384's provider, none shares an email or a LinkedIn URL with another person row, and none shares a normalized name with another
      person holding a live affiliation on the same org.
    - 68987's BcRegistry enrichment row (61564) is unchanged and still ok, and BcRegistryLastCheckedAtUtc was left as it was, so a later
      refresh could write A-TEAM CONSTRUCTION back. BcRegistryProvider.IsLikelyMatch now needs whole words (5 Nov), which holds once a
      Worker carrying it is deployed; until then the risk stands. Board row 1636 has the class.
    - The 1838121 WarmChannel fact's Confidence High did not match its body's "unconfirmed". Migration 383 retired that fact.
    - tools/BdIntegrityCheck was not recorded before or after this data change, so the two reports its module rule asks for cannot be
      diffed. The run after it, on 5 Nov, is named in docs/HANDOVER.md.

    People were keyed: NaturalKey = SHA1(lower(trim(email))); with no email, SHA1(normalized name|org:id).
    Chad Gould already exists as IntelPerson 643 under a name key, so his row gets the email by COALESCE and a new affiliation
    rather than a second person. An existing email is never overwritten.

    Correction: org 68987 "Team Construction" carried BC registry name A-TEAM CONSTRUCTION, a different business (SP, 2004).
    Org 70567 holds the real registry record. The wrong fields are cleared and the pair is recorded as DuplicateOf; the merge
    itself is left to BdCanonicalDedup because a merge deletes the loser row.

    People and affiliations MERGE on NaturalKey and facts go in by NOT EXISTS, but the file as a whole cannot run twice (above).
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

IF (SELECT COUNT(*) FROM opportunities.CanonicalOrg WHERE Id IN (1838121, 1838122, 1838123, 54726, 70567, 68987)) <> 6
    THROW 50001, N'382: an expected org row is missing', 1;

DECLARE @provider NVARCHAR(120) = N'Apollo-Verified-2026-11-04';
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

DECLARE @c TABLE (
    OrgId BIGINT, Person NVARCHAR(200), Title NVARCHAR(200), Email NVARCHAR(200), Linkedin NVARCHAR(400),
    NormName NVARCHAR(200), NormTitle NVARCHAR(200), PKey CHAR(40), AffKey CHAR(40), PersonId BIGINT);

INSERT INTO @c (OrgId, Person, Title, Email, Linkedin) VALUES
 (1838121, N'Gurupdesh Dhaliwal', N'President', N'gdhaliwal@bridgestar.ca', N'http://www.linkedin.com/in/gurupdesh-dhaliwal-b471a748'),
 (1838121, N'Graham Chatwin', N'Director of Construction', N'gchatwin@bridgestar.ca', N'http://www.linkedin.com/in/graham-chatwin-4a780a5a'),
 (1838121, N'Melissa Bell', N'Director of Operations', N'mbell@bridgestar.ca', N'http://www.linkedin.com/in/melissabellbc'),
 (1838121, N'Scott Wandler', N'Chief Financial Officer', N'swandler@bridgestar.ca', N'http://www.linkedin.com/in/scott-wandler-cpa-ca-79b61527'),
 (1838121, N'Rashmeen Malhotra', N'Technical Project Manager', N'rmalhotra@bridgestar.ca', N'http://www.linkedin.com/in/rashmeenmalhotra'),
 (1838122, N'Geoff Stretch', N'President', N'geoff@stretchconstruction.ca', N'http://www.linkedin.com/in/geoff-stretch-a955a016b'),
 (1838122, N'Justin Perrin', N'Senior Project Manager', N'justin.perrin@stretchconstruction.ca', N'http://www.linkedin.com/in/justin-perrin-a690bbba'),
 (1838122, N'Henrik Drago', N'Construction Manager', NULL, N'http://www.linkedin.com/in/henrik-drago-94073a292'),
 (54726, N'Ryan Van Damme', N'Chief Executive Officer', N'rvandamme@icdev.ca', N'http://www.linkedin.com/in/ryan-van-damme-4b142a57'),
 (54726, N'Janet Sokolowski', N'Chief Operating Officer', N'jgauthier@icdev.ca', N'http://www.linkedin.com/in/janet-sokolowski-gauthier-p-eng-13979997'),
 (54726, N'Michal Kubasiewicz', N'Vice President, Land Acquisition & Development', N'mkubasiewicz@icdev.ca', N'http://www.linkedin.com/in/michal-kubasiewicz-rpp-mcip-mba-56a7b827'),
 (54726, N'Steve Van Mil', N'Vice President, Construction Management', N'svanmil@icdev.ca', N'http://www.linkedin.com/in/steve-van-mil-50225a5b'),
 (54726, N'Andrew Meads', N'Lead Structural Engineer', N'ameads@icdev.ca', N'http://www.linkedin.com/in/andrew-meads-59aa49184'),
 (54726, N'Stephen Oberlin', N'Senior Design Lead', N'soberlin@icdev.ca', N'http://www.linkedin.com/in/stephen-oberlin-8936aa199'),
 (54726, N'Kelly Baumgartner', N'Project Architect', N'kbaumgartner@icdev.ca', N'http://www.linkedin.com/in/kelly-baumgartner-2abb0a97');

UPDATE @c SET
  NormName = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
             LOWER(LTRIM(RTRIM(Person))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+',''),
  NormTitle = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
             LOWER(LTRIM(RTRIM(Title))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','');

UPDATE @c SET PKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(
    CASE WHEN Email IS NOT NULL THEN LOWER(LTRIM(RTRIM(Email)))
         ELSE NormName + '|org:' + CAST(OrgId AS VARCHAR(20)) END AS VARCHAR(8000))), 2);

DECLARE @enr TABLE (EnrId BIGINT, OrgId BIGINT);
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT DISTINCT OrgId FROM @c UNION SELECT 70567) AS S
  ON 1 = 0
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, LastRefreshAtUtc, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @provider, N'ok', 1, @now, @now, @now)
OUTPUT inserted.Id, inserted.CanonicalOrgId INTO @enr (EnrId, OrgId);

MERGE opportunities.IntelPerson WITH (HOLDLOCK) AS T
USING (SELECT c.PKey, c.Person, c.NormName, c.Email, c.Linkedin, e.EnrId FROM @c c JOIN @enr e ON e.OrgId = c.OrgId) AS S
   ON T.NaturalKey = S.PKey
WHEN MATCHED THEN UPDATE SET
   Email = COALESCE(T.Email, S.Email), LinkedinUrl = COALESCE(T.LinkedinUrl, S.Linkedin), LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc,
    DisplayName, NormalizedName, Email, EmailSource, EmailConfidence, LinkedinUrl)
   VALUES
   (@provider, S.EnrId, N'High', S.PKey, @now, @now, S.Person, S.NormName, S.Email,
    CASE WHEN S.Email IS NULL THEN NULL ELSE N'Apollo' END, CASE WHEN S.Email IS NULL THEN NULL ELSE 90 END, S.Linkedin);

UPDATE c SET c.PersonId = p.Id FROM @c c JOIN opportunities.IntelPerson p ON p.NaturalKey = c.PKey;

/* Chad Gould: the existing person, the real company's org */
UPDATE opportunities.IntelPerson
   SET Email = COALESCE(Email, N'cgould@teamconstruction.ca'), EmailSource = COALESCE(EmailSource, N'Apollo'),
       EmailConfidence = COALESCE(EmailConfidence, 90), LinkedinUrl = COALESCE(LinkedinUrl, N'http://www.linkedin.com/in/chad-gould-63bb1360'),
       LastSeenAtUtc = @now, UpdatedAtUtc = @now
 WHERE Id = 643 AND DisplayName = N'Chad Gould';
IF @@ROWCOUNT <> 1 THROW 50002, N'382: Chad Gould row 643 not as expected', 1;
INSERT INTO @c (OrgId, Person, Title, NormTitle, PersonId) VALUES (70567, N'Chad Gould', N'President', N'president', 643);

UPDATE @c SET AffKey = CONVERT(CHAR(40), HASHBYTES('SHA1',
   CAST(CAST(PersonId AS VARCHAR(20)) + '|' + CAST(OrgId AS VARCHAR(20)) + '|' + NormTitle AS VARCHAR(8000))), 2);

MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T
USING (SELECT c.AffKey, c.PersonId, c.OrgId, c.Title, e.EnrId FROM @c c JOIN @enr e ON e.OrgId = c.OrgId) AS S
   ON T.NaturalKey = S.AffKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey, FirstSeenAtUtc, LastSeenAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
   VALUES (@provider, S.EnrId, N'High', S.AffKey, @now, @now, S.PersonId, S.OrgId, S.Title, 1);

/* Correction at source: the wrong registry record on 68987 */
UPDATE opportunities.CanonicalOrg
   SET BcRegistryTopicId = NULL, BcRegistryLegalName = NULL, BcRegistryEntityType = NULL, BcRegistryStatus = NULL,
       BcRegistryIncorporationDate = NULL, BcRegistryJurisdiction = NULL, BcRegistryBusinessNumber = NULL, BcRegistryRegisteredOffice = NULL, UpdatedAtUtc = @now
 WHERE Id = 68987 AND BcRegistryLegalName = N'A-TEAM CONSTRUCTION';

/* Facts. No website is written: it stays on 68987, as the DuplicateOf fact says, and the fuzzy-key and domain writes stay with the resolver */
INSERT INTO opportunities.OrgFact
    (NaturalKey, CanonicalOrgId, FactType, Body, SourceUrl, SourceRef, ObservedAtUtc, Confidence, CreatedAtUtc, CreatedBy)
SELECT CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2), v.org, v.ft, v.body, v.url,
       N'Conor brief: Bridgestar, Team, Ironclad 2026-11-04', CAST('2026-11-04' AS datetimeoffset), v.conf, SYSUTCDATETIME(), N'BrainDecompose-2026-11-04'
FROM (VALUES
 ('1838121|warm|2026-11-04', 1838121, N'WarmChannel', N'High',
  N'Graham Chatwin, Director of Construction since Oct 2026, was at Mission Group May 2016 to Jul 2025 (Apollo). KOR client: Mission Group, $204,949 invoiced on 2 jobs. Conor Murtagh is PM and principal on 31101 955 Timmins St, 426.5 h from Jun 2024; Rory Beirne managed phases of 30773 Ella, 2017-19. Whether Graham dealt with KOR on either is unconfirmed: he is not in Deltek contacts.', NULL),
 ('1838121|bench|2026-11-04', 1838121, N'MarketFocus', N'High',
  N'Founded 2020, Kelowna owner-developer-manager, about 6 staff (Apollo estimate). Four of five senior people joined in 2026: CFO Scott Wandler Mar, Technical PM Rashmeen Malhotra Mar, Director of Operations Melissa Bell Jun, Director of Construction Graham Chatwin Oct. Portfolio: Backstreet 64 homes proposed, 32 Backstreet Blvd Penticton; Nova on the Park 45 rentals 5 storeys, 681 Wardlaw Ave Kelowna; Astra 48 homes + retail 5 storeys, 123 Front St Penticton; Sutherland 16 units, 598 Sutherland Ave Kelowna; Chelmsford Plaza retail, Ontario.', N'https://bridgestar.ca/properties/'),
 ('1838121|delivery|2026-11-04', 1838121, N'DeliveryModel', N'High',
  N'Stretch Construction is contractor on Front St and Wardlaw (maisedesign.ca, stretchconstruction.ca). Front St permit drawings DP PL2023-9703 name Stretch as client and copyright holder, Sherri Turpin Architect, WSP Kelowna for landscape, Maise Design for interiors. Structural engineer on both: not found on any readable public record. Nothing yet ties Stretch to Backstreet.', N'https://www.penticton.ca/sites/default/files/docs/city-hall/news/Public%20Notices/2024/2024-05-09%20Public%20Notice%20(123%20Front%20St).pdf'),
 ('1838121|deltek|2026-11-04', 1838121, N'DeltekLink', N'High',
  N'No Deltek client under Bridgestar. Only address tie: 30793-01, 32-56 Backstreet Blvd, client Wardman Group of Companies, 2017, 1 h John Markulin, $200 fee. No evidence Wardman and Bridgestar are connected. The 0.31-acre site at 32 Backstreet sold for $1.585M in late June 2024 (Castanet, buyer not named there).', N'https://www.castanet.net/news/Penticton/500909/Coveted-downtown-Penticton-property-sells-for-nearly-1-6M'),
 ('1838122|delivery|2026-11-04', 1838122, N'DeliveryModel', N'High',
  N'Kelowna design-to-delivery commercial and multi-family contractor, 300-1680 Water St, about 7 staff (Apollo estimate). Bridgestar''s contractor on 123 Front St and 681 Wardlaw. Richard Boerger is the contact on the Front St drawings (richard.boerger@stretchconstruction.ca). Its structural engineer is the deciding unknown for any Bridgestar pursuit.', N'https://www.stretchconstruction.ca/projects/current-projects'),
 ('1838123|work|2026-11-04', 1838123, N'MarketFocus', N'High',
  N'Penticton architect, turpinarchitect@outlook.com, 587-876-7616. Architect of record on Bridgestar''s 123 Front St, Penticton (DP PL2023-9703, drawings to 12 Apr 2024). Also letter of 4 Mar 2024 on 425-455 Vista Rd, Kelowna.', NULL),
 ('54726|selfperforms|2026-11-04', 54726, N'SelfPerformsStructural', N'Medium',
  N'Vertically integrated: land, design, engineering, construction, rentals; site says buildings are designed by its technicians and engineers. Apollo lists Andrew Meads as Lead Structural Engineer since 2022, Winnipeg, also listed current at DGH Engineering; about 6 in engineering of about 130 staff. Founder Ryan Van Damme and VP Steve Van Mil came from Seymour Pacific. Who seals BC buildings: not confirmed.', N'https://www.ironcladdevelopments.ca/about'),
 ('54726|risk|2026-11-04', 54726, N'RiskNote', N'High',
  N'Worker death at Mission Heights, 2241 Springfield Rd, Kelowna, 14 Jun 2024. RCMP and WorkSafeBC investigated (Global News 18 Jun 2024). No published outcome found on 4 Nov 2026. Status conflict: Ironclad lists the address as Parkview Valley, complete; Develop Kelowna lists Mission Heights, under construction.', N'https://globalnews.ca/news/10573360/kelowna-construction-site-death-investigation'),
 ('68987|dup|2026-11-04', 68987, N'DuplicateOf', N'High',
  N'Duplicate of org 70567 Team Construction Management (1981) Ltd., the record holding the real BC registry entry. This row wrongly carried A-TEAM CONSTRUCTION (SP, 2004), cleared 4 Nov 2026. Website and people still sit here; merge through BdCanonicalDedup.', N'https://www.teamconstruction.ca/about/'),
 ('70567|profile|2026-11-04', 70567, N'DeliveryModel', N'High',
  N'Kelowna construction manager, founded 1981, about 16 staff (Apollo). Design-build CCDC 14, CM CCDC 5A and 5B, GMP, stipulated sum, development management; Apollo reports own framing and concrete crews. Chad Gould President since 2018. No Deltek client under this name.', N'https://www.teamconstruction.ca/about/')
) AS v(k, org, ft, conf, body, url)
WHERE NOT EXISTS (SELECT 1 FROM opportunities.OrgFact e WHERE e.NaturalKey = CONVERT(varchar(40), HASHBYTES('SHA1', v.k), 2));

SELECT 'people on these orgs' AS Section, a.CanonicalOrgId, COUNT(*) AS Affiliations, SUM(CASE WHEN p.Email IS NOT NULL THEN 1 ELSE 0 END) AS WithEmail
  FROM opportunities.IntelPersonAffiliation a JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
 WHERE a.CanonicalOrgId IN (1838121, 1838122, 1838123, 54726, 70567) AND a.RetiredAtUtc IS NULL GROUP BY a.CanonicalOrgId ORDER BY a.CanonicalOrgId;
SELECT 'facts' AS Section, CanonicalOrgId, COUNT(*) AS Facts FROM opportunities.OrgFact WHERE CreatedBy = N'BrainDecompose-2026-11-04' GROUP BY CanonicalOrgId ORDER BY CanonicalOrgId;
SELECT 'registry on 68987' AS Section, Id, BcRegistryLegalName FROM opportunities.CanonicalOrg WHERE Id IN (68987, 70567);

COMMIT;
