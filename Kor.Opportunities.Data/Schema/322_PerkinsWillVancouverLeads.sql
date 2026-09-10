-- Migration 322 (2026-09-10): bank the three Perkins&Will Vancouver leads that
-- the Hunter EMAIL-FINDER resolved, which the domain roster sweep had missed.
--
-- The roster endpoint returns who Hunter happens to hold for a domain; the
-- finder takes a named person and returns their address. These three were named
-- on the Vancouver studio page but absent from the roster, so they existed in
-- the brief and nowhere in the Brain.
--
--   Shauna Bryce      shauna.bryce@perkinswill.com     conf 98   Cultural and Civic
--   Jeff Doble        jeff.doble@perkinswill.com       conf 95   Director of Transportation
--   Kerri Henderson   kerri.henderson@perkinswill.com  conf 97   Advisory Services
--
-- Jeff Doble is the one that matters: the firm has two open Regional Practice
-- Leader — Transit & Transportation Architecture postings, and he is the
-- transportation name already sitting in Vancouver.
--
-- Follows reference_intelperson_ingest_contract exactly, so the app's own
-- enrichment MERGEs onto these rows instead of duplicating them:
--   * Person NaturalKey      = SHA1_HEX_UPPER(lower(trim(email)))  — email-first
--   * Affiliation NaturalKey = SHA1_HEX_UPPER(personId|orgId|normalize(Title))
--   * HASHBYTES over VARCHAR, never NVARCHAR — UTF-16 bytes hash differently
--   * Email written with COALESCE so an on-file address is never clobbered
--   * SourceEnrichmentId is NOT NULL on both tables and FKs to
--     CanonicalOrgEnrichment, so the parent row is created first
--
-- ⚠⚠ TWO OF THE THREE WERE ALREADY ON FILE WITHOUT AN EMAIL, and that is the
--    whole difficulty. The person key is email-FIRST, so keying these three off
--    their new addresses mints a SECOND row for anyone already keyed off their
--    name — the exact 444-row collision the contract's own data-quality note
--    describes. A dry run caught it:
--
--      Shauna Bryce   person 9663, DecisionMakers, HAS the address already
--      Jeff Doble     person 4525, DecisionMakers, NO address  ← would duplicate
--      Kerri Henderson  genuinely absent
--
--    So this migration RESOLVES BY NAME FIRST and only inserts where no row for
--    that person exists at this org. An existing row gets the address, not a twin.
--
-- ⚠ It also does NOT add an affiliation to anyone who already has one here.
--   Shauna Bryce carries four near-identical title rows already ("Associate
--   Principal, Cultural and Civic" twice, plus two Specialty Leader phrasings);
--   a fifth spelling helps nobody. Those four are pre-existing and are left
--   alone deliberately — collapsing them is a dedup pass, not this migration.
--
-- ⭐ FOUND WHILE CHECKING THAT, and worth more than this migration is:
--   ALL FOUR of Shauna Bryce's affiliations at 69688 are RETIRED. Her only live
--   one is at canonical org 69249, "Busby + Associates Architects" — Peter
--   Busby's firm, which Perkins&Will acquired in 2004 and which became its
--   Vancouver studio. SEVEN live people sit there, every one on an
--   @perkinswill.com address, with fuller titles than 69688 holds:
--
--     Derek Newby, Jana Foit, Adrian Watson, Ryan Bragg, Shauna Bryce,
--     Kathy Wardle, Peter Busby
--
--   So the Vancouver leadership is split across two org records and the
--   "135 people" figure understates it. NOT merged here: an acquired
--   predecessor is not a spelling variant, and the dedup guards added on
--   2026-09-04 exist to stop exactly that kind of confident over-merge. It
--   needs a decision, not a migration written at speed.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @org bigint = 69688;
DECLARE @provider nvarchar(100) = N'HunterEmailFinder';
DECLARE @now datetimeoffset = sysdatetimeoffset();

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE Id = @org)
    THROW 50322, 'CanonicalOrg 69688 (Perkins&Will) is missing.', 1;

-- 1. One parent enrichment row per (org, provider).
DECLARE @enr bigint;
SELECT @enr = Id FROM opportunities.CanonicalOrgEnrichment
WHERE CanonicalOrgId = @org AND ProviderName = @provider;

IF @enr IS NULL
BEGIN
    INSERT INTO opportunities.CanonicalOrgEnrichment
        (CanonicalOrgId, ProviderName, Status, Attempts,
         LastRefreshAtUtc, LastAttemptAtUtc, Notes, CreatedAtUtc, UpdatedAtUtc)
    VALUES (@org, @provider, N'ok', 1, @now, @now,
            N'Hunter email-finder, three named Vancouver studio people the domain roster did not return.',
            @now, @now);
    SET @enr = SCOPE_IDENTITY();
END
ELSE
BEGIN
    UPDATE opportunities.CanonicalOrgEnrichment
    SET Status = N'ok', Attempts = Attempts + 1,
        LastRefreshAtUtc = @now, LastAttemptAtUtc = @now, UpdatedAtUtc = @now
    WHERE Id = @enr;
END;

-- 2. The three people.
IF OBJECT_ID('tempdb..#lead') IS NOT NULL DROP TABLE #lead;
CREATE TABLE #lead (
    DisplayName nvarchar(200) NOT NULL,
    Email       nvarchar(320) NOT NULL,
    Title       nvarchar(200) NULL,
    EmailConf   tinyint       NOT NULL);

-- Titles are what the firm's own Vancouver studio page lists them under. Doble
-- is deliberately NOT given "Director of Transportation": that came from the
-- Hunter position field, our DecisionMakers record says "Principal,
-- Transportation", the studio page publishes no title at all, and an asserted
-- title nobody can confirm is worse than a discipline everybody can.
INSERT INTO #lead (DisplayName, Email, Title, EmailConf) VALUES
    (N'Shauna Bryce',    N'shauna.bryce@perkinswill.com',    N'Practice Leader - Cultural and Civic', 98),
    (N'Jeff Doble',      N'jeff.doble@perkinswill.com',      N'Transportation',                       95),
    (N'Kerri Henderson', N'kerri.henderson@perkinswill.com', N'Advisory Services',                    97);

-- Resolve each lead to a person that already exists, by email if we hold one and
-- otherwise by name at THIS org. Only what is left over is genuinely new.
ALTER TABLE #lead ADD ExistingPersonId bigint NULL;

UPDATE l SET ExistingPersonId = p.Id
FROM #lead l
JOIN opportunities.IntelPerson p
  ON p.Email = l.Email AND p.RetiredAtUtc IS NULL;

UPDATE l SET ExistingPersonId = x.Id
FROM #lead l
CROSS APPLY (
    SELECT TOP 1 p.Id
    FROM opportunities.IntelPerson p
    JOIN opportunities.IntelPersonAffiliation a
      ON a.IntelPersonId = p.Id AND a.CanonicalOrgId = @org AND a.RetiredAtUtc IS NULL
    WHERE p.RetiredAtUtc IS NULL
      AND p.DisplayName = l.DisplayName
      AND (p.Email IS NULL OR p.Email = l.Email)
    ORDER BY p.Id) AS x
WHERE l.ExistingPersonId IS NULL;

SELECT 'how each lead resolved' AS Section;
SELECT DisplayName, Email,
       CASE WHEN ExistingPersonId IS NULL THEN 'NEW - insert'
            ELSE 'EXISTING person ' + CONVERT(varchar(20), ExistingPersonId) + ' - fill in the address'
       END AS Decision
FROM #lead ORDER BY DisplayName;

-- Fill the address in on the people we already hold. COALESCE so an on-file
-- address is never overwritten, only a gap filled.
UPDATE p
SET Email             = COALESCE(p.Email, l.Email),
    EmailSource       = COALESCE(p.EmailSource, N'Hunter'),
    EmailConfidence   = CASE WHEN p.Email IS NULL THEN l.EmailConf
                             ELSE COALESCE(p.EmailConfidence, l.EmailConf) END,
    EmailCheckedAtUtc = @now,
    LastSeenAtUtc     = @now,
    Corroborations    = p.Corroborations + 1,
    UpdatedAtUtc      = @now
FROM opportunities.IntelPerson p
JOIN #lead l ON l.ExistingPersonId = p.Id;

MERGE opportunities.IntelPerson AS T
USING (
    SELECT DisplayName, Email, EmailConf,
           LOWER(LTRIM(RTRIM(DisplayName))) AS NormalizedName,
           CONVERT(CHAR(40), HASHBYTES('SHA1',
               CAST(LOWER(LTRIM(RTRIM(Email))) AS VARCHAR(8000))), 2) AS NaturalKey
    FROM #lead WHERE ExistingPersonId IS NULL) AS S
ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    -- Never clobber an address already on file; only fill a gap.
    T.Email           = COALESCE(T.Email, S.Email),
    T.EmailSource     = COALESCE(T.EmailSource, N'Hunter'),
    T.EmailConfidence = COALESCE(T.EmailConfidence, S.EmailConf),
    T.EmailCheckedAtUtc = @now,
    T.LastSeenAtUtc   = @now,
    T.Corroborations  = T.Corroborations + 1,
    T.RetiredAtUtc    = NULL,
    T.RetiredReason   = NULL,
    T.UpdatedAtUtc    = @now
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
     DisplayName, NormalizedName, Email, Corroborations,
     EmailSource, EmailConfidence, EmailCheckedAtUtc)
    VALUES
    (@provider, @enr, N'High', S.NaturalKey,
     @now, @now, @now, @now,
     S.DisplayName, S.NormalizedName, S.Email, 1,
     N'Hunter', S.EmailConf, @now);

-- 3. An affiliation to Perkins&Will, but ONLY for someone who has none here.
--    Anyone already affiliated has a title from earlier research; a second
--    spelling of the same job is the duplicate this table already suffers from.
--    normalize() per the contract: lowercase, then strip  . , ' - & / ( ) +
--    and spaces.
MERGE opportunities.IntelPersonAffiliation AS T
USING (
    SELECT p.Id AS IntelPersonId, @org AS CanonicalOrgId, l.Title,
           CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(
               CAST(p.Id AS varchar(20)) + '|' + CAST(@org AS varchar(20)) + '|' +
               REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                   LOWER(ISNULL(l.Title, N'')),
                   '.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+',''),' ','')
               AS VARCHAR(8000))), 2) AS NaturalKey
    FROM #lead l
    JOIN opportunities.IntelPerson p ON p.Email = l.Email AND p.RetiredAtUtc IS NULL
    WHERE NOT EXISTS (
        SELECT 1 FROM opportunities.IntelPersonAffiliation a
        WHERE a.IntelPersonId = p.Id AND a.CanonicalOrgId = @org
          AND a.RetiredAtUtc IS NULL)) AS S
ON T.NaturalKey = S.NaturalKey
WHEN MATCHED THEN UPDATE SET
    T.Title         = COALESCE(T.Title, S.Title),
    T.IsCurrent     = 1,
    T.LastSeenAtUtc = @now,
    T.RetiredAtUtc  = NULL,
    T.RetiredReason = NULL,
    T.UpdatedAtUtc  = @now
WHEN NOT MATCHED THEN INSERT
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
     IntelPersonId, CanonicalOrgId, Title, IsCurrent, Notes)
    VALUES
    (@provider, @enr, N'High', S.NaturalKey,
     @now, @now, @now, @now,
     S.IntelPersonId, S.CanonicalOrgId, S.Title, 1,
     N'Named on the Perkins&Will Vancouver studio page; address from the Hunter email-finder.');

SELECT 'the three leads, as now held' AS Section;
SELECT p.DisplayName, p.Email, p.EmailConfidence, a.Title, a.IsCurrent
FROM opportunities.IntelPerson p
JOIN opportunities.IntelPersonAffiliation a
  ON a.IntelPersonId = p.Id AND a.CanonicalOrgId = @org
WHERE p.Email IN (N'shauna.bryce@perkinswill.com', N'jeff.doble@perkinswill.com',
                  N'kerri.henderson@perkinswill.com')
ORDER BY p.DisplayName;

SELECT 'duplicate check: one person row per address' AS Section;
SELECT p.Email, COUNT(*) AS Rows
FROM opportunities.IntelPerson p
WHERE p.Email IN (N'shauna.bryce@perkinswill.com', N'jeff.doble@perkinswill.com',
                  N'kerri.henderson@perkinswill.com')
GROUP BY p.Email;

SELECT 'Perkins&Will people held, current affiliations' AS Section;
SELECT COUNT(DISTINCT a.IntelPersonId) AS DistinctPeople
FROM opportunities.IntelPersonAffiliation a
WHERE a.CanonicalOrgId = @org AND a.RetiredAtUtc IS NULL;
GO
