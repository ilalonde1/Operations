/* Connection-scoped like every other migration — no USE. Run against the
   intended database's connection. APPLY WITH QUOTED_IDENTIFIER ON (SSMS
   default; sqlcmd needs -I) — the proc updates tables with filtered indexes,
   and a QI-OFF-stamped proc throws 1934 at runtime.
   APPLIED to KOR-APP01 KorOpportunitiesDb 2026-07-11 (verified: pre-fix repro
   threw 2627; post-fix the retired row is resurrected in place). */

/* =====================================================================
   277 — usp_ResolveOrCreateIntelPerson: resurrect-on-rediscovery.
   ---------------------------------------------------------------------
   Supersedes 265's proc body with ONE addition. 265's NaturalKey lookup
   filters RetiredAtUtc IS NULL, but UQ_IntelPerson_NaturalKey is unfiltered
   (spans retired rows) — so re-discovering a person retired for dormancy
   fell through to INSERT with the same key, threw 2627, and rolled back the
   caller's ENTIRE enrichment batch. Permanent, silent, self-repeating: every
   later touch of that org hit the same poison record.

   Fix mirrors the drain's TOCTOU-guarded person resurrect (91ad59cc /
   83fd5043): when the live lookups miss but a RETIRED row holds the
   NaturalKey, reclaim that row atomically (guarded UPDATE re-checks
   RetiredAtUtc IS NOT NULL) instead of inserting a doomed duplicate.

   Everything else is byte-identical to 265.
   ===================================================================== */

CREATE OR ALTER PROCEDURE opportunities.usp_ResolveOrCreateIntelPerson
    @displayName        NVARCHAR(400),
    @email              NVARCHAR(400) = NULL,
    @linkedinUrl        NVARCHAR(800) = NULL,
    @phone              NVARCHAR(50) = NULL,
    @notes              NVARCHAR(MAX) = NULL,
    @orgId              BIGINT = NULL,
    @sourceProviderName NVARCHAR(200) = NULL,
    @emailSource        NVARCHAR(50) = NULL,
    @emailConfidence    INT = NULL,
    @personId           BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @personId = NULL;

    DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();
    DECLARE @provider NVARCHAR(60) = LEFT(COALESCE(NULLIF(LTRIM(RTRIM(@sourceProviderName)), N''), N'IntelPersonResolver'), 60);
    DECLARE @cleanDisplayName NVARCHAR(400) = NULLIF(LTRIM(RTRIM(@displayName)), N'');
    DECLARE @cleanEmail NVARCHAR(200) = LEFT(NULLIF(LTRIM(RTRIM(@email)), N''), 200);
    DECLARE @cleanLinkedinUrl NVARCHAR(500) = LEFT(NULLIF(LTRIM(RTRIM(@linkedinUrl)), N''), 500);
    DECLARE @cleanPhone NVARCHAR(50) = LEFT(NULLIF(LTRIM(RTRIM(@phone)), N''), 50);
    DECLARE @cleanNotes NVARCHAR(MAX) = NULLIF(LTRIM(RTRIM(@notes)), N'');
    DECLARE @emailKey NVARCHAR(200) = LOWER(COALESCE(@cleanEmail, N''));
    DECLARE @linkedinKey NVARCHAR(500) = LOWER(COALESCE(@cleanLinkedinUrl, N''));
    DECLARE @normalizedName NVARCHAR(200) =
        LEFT(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
             LOWER(LTRIM(RTRIM(COALESCE(@displayName, N'')))),
             N' ', N''), N'.', N''), N',', N''), N'''', N''), N'-', N''), N'&', N''), N'/', N''), N'(', N''), N')', N''), N'+', N''), 200);
    DECLARE @emailSourceForWrite NVARCHAR(20) = LEFT(NULLIF(LTRIM(RTRIM(@emailSource)), N''), 20);
    DECLARE @emailConfidenceForWrite TINYINT =
        CASE
            WHEN @emailConfidence IS NULL THEN NULL
            WHEN @emailConfidence < 0 THEN 0
            WHEN @emailConfidence > 100 THEN 100
            ELSE CONVERT(TINYINT, @emailConfidence)
        END;
    DECLARE @sourceEnrichmentId BIGINT = NULL;
    DECLARE @foundId BIGINT = NULL;
    DECLARE @keyMaterial NVARCHAR(4000);
    DECLARE @naturalKey CHAR(40);

    IF @cleanDisplayName IS NULL
    BEGIN
        THROW 50000, 'displayName is required.', 1;
    END;

    IF @normalizedName = N''
    BEGIN
        THROW 50002, 'displayName normalizes to an empty IntelPerson.NormalizedName.', 1;
    END;

    BEGIN TRAN;

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    SELECT TOP (1) @foundId = p.Id
    FROM opportunities.IntelPerson AS p WITH (UPDLOCK, HOLDLOCK)
    WHERE p.RetiredAtUtc IS NULL
      AND @emailKey <> N''
      AND LOWER(LTRIM(RTRIM(COALESCE(p.Email, N'')))) = @emailKey
    ORDER BY p.Id;

    IF @foundId IS NULL
    BEGIN
        SELECT TOP (1) @foundId = p.Id
        FROM opportunities.IntelPerson AS p WITH (UPDLOCK, HOLDLOCK)
        WHERE p.RetiredAtUtc IS NULL
          AND @linkedinKey <> N''
          AND LOWER(LTRIM(RTRIM(COALESCE(p.LinkedinUrl, N'')))) = @linkedinKey
        ORDER BY p.Id;
    END;

    IF @foundId IS NULL AND @orgId IS NOT NULL AND @normalizedName <> N''
    BEGIN
        SELECT TOP (1) @foundId = p.Id
        FROM opportunities.IntelPerson AS p WITH (UPDLOCK, HOLDLOCK)
        JOIN opportunities.IntelPersonAffiliation AS a WITH (UPDLOCK, HOLDLOCK)
            ON a.IntelPersonId = p.Id
           AND a.CanonicalOrgId = @orgId
           AND a.IsCurrent = 1
           AND a.RetiredAtUtc IS NULL
        WHERE p.RetiredAtUtc IS NULL
          AND p.NormalizedName = @normalizedName
        ORDER BY p.Id;
    END;

    -- Adopt a single org-less discovery stub: a person created name-only by
    -- first-pass discovery (BdQueueDrainIngest) that has no active affiliations
    -- yet. Only when EXACTLY ONE active stub matches the normalized name -
    -- ambiguous or already-affiliated names fall through to create, never
    -- auto-merge. This converges enrichment onto the stub instead of
    -- duplicating it.
    IF @foundId IS NULL AND @normalizedName <> N''
    BEGIN
        DECLARE @stubCount INT;
        SELECT @stubCount = COUNT(*), @foundId = MIN(p.Id)
        FROM opportunities.IntelPerson AS p WITH (UPDLOCK, HOLDLOCK)
        WHERE p.RetiredAtUtc IS NULL
          AND p.NormalizedName = @normalizedName
          AND NOT EXISTS (
                SELECT 1 FROM opportunities.IntelPersonAffiliation AS a
                WHERE a.IntelPersonId = p.Id AND a.RetiredAtUtc IS NULL);
        IF @stubCount <> 1 SET @foundId = NULL;
    END;

    IF @foundId IS NOT NULL
    BEGIN
        UPDATE opportunities.IntelPerson
        SET Email = COALESCE(Email, @cleanEmail),
            LinkedinUrl = COALESCE(LinkedinUrl, @cleanLinkedinUrl),
            Phone = COALESCE(Phone, @cleanPhone),
            Notes = COALESCE(Notes, @cleanNotes),
            Corroborations = Corroborations + 1,
            EmailSource =
                CASE
                    WHEN Email IS NULL AND @cleanEmail IS NOT NULL THEN COALESCE(EmailSource, @emailSourceForWrite)
                    ELSE EmailSource
                END,
            EmailConfidence =
                CASE
                    WHEN Email IS NULL AND @cleanEmail IS NOT NULL THEN COALESCE(EmailConfidence, @emailConfidenceForWrite)
                    ELSE EmailConfidence
                END,
            LastSeenAtUtc = @now,
            UpdatedAtUtc = @now
        WHERE Id = @foundId;

        SET @personId = @foundId;

        COMMIT TRAN;
        RETURN;
    END;

    IF @emailKey = N'' AND @linkedinKey = N'' AND @orgId IS NULL
    BEGIN
        THROW 50003, 'email, linkedinUrl, or orgId is required; bare-name IntelPerson resolution/create is not allowed.', 1;
    END;

    SET @keyMaterial =
        CASE
            WHEN @emailKey <> N'' THEN @emailKey
            WHEN @linkedinKey <> N'' THEN @linkedinKey
            WHEN @normalizedName <> N'' AND @orgId IS NOT NULL
                THEN @normalizedName + N'|org:' + CONVERT(NVARCHAR(20), @orgId)
            ELSE @normalizedName
        END;

    SET @naturalKey = CONVERT(CHAR(40), HASHBYTES('SHA1', CAST(@keyMaterial AS VARCHAR(8000))), 2);

    SELECT TOP (1) @foundId = p.Id
    FROM opportunities.IntelPerson AS p WITH (UPDLOCK, HOLDLOCK)
    WHERE p.RetiredAtUtc IS NULL
      AND p.NaturalKey = @naturalKey
    ORDER BY p.Id;

    IF @foundId IS NOT NULL
    BEGIN
        UPDATE opportunities.IntelPerson
        SET Email = COALESCE(Email, @cleanEmail),
            LinkedinUrl = COALESCE(LinkedinUrl, @cleanLinkedinUrl),
            Phone = COALESCE(Phone, @cleanPhone),
            Notes = COALESCE(Notes, @cleanNotes),
            Corroborations = Corroborations + 1,
            EmailSource =
                CASE
                    WHEN Email IS NULL AND @cleanEmail IS NOT NULL THEN COALESCE(EmailSource, @emailSourceForWrite)
                    ELSE EmailSource
                END,
            EmailConfidence =
                CASE
                    WHEN Email IS NULL AND @cleanEmail IS NOT NULL THEN COALESCE(EmailConfidence, @emailConfidenceForWrite)
                    ELSE EmailConfidence
                END,
            LastSeenAtUtc = @now,
            UpdatedAtUtc = @now
        WHERE Id = @foundId;

        SET @personId = @foundId;

        COMMIT TRAN;
        RETURN;
    END;

    /* 277: resurrect-on-rediscovery. The live lookup above skips retired rows,
       but UQ_IntelPerson_NaturalKey spans them — inserting the same key would
       throw 2627 and roll back the caller's whole batch. When a RETIRED row
       holds this NaturalKey, reclaim it atomically instead. The guarded UPDATE
       re-checks RetiredAtUtc IS NOT NULL (TOCTOU pattern from 83fd5043) so a
       concurrent resurrect can't double-fire; identity anchors are stable, so
       reclaiming the row preserves all history hanging off its Id. */
    DECLARE @retiredId BIGINT = NULL;
    SELECT TOP (1) @retiredId = p.Id
    FROM opportunities.IntelPerson AS p WITH (UPDLOCK, HOLDLOCK)
    WHERE p.RetiredAtUtc IS NOT NULL
      AND p.NaturalKey = @naturalKey
    ORDER BY p.Id;

    IF @retiredId IS NOT NULL
    BEGIN
        UPDATE opportunities.IntelPerson
        SET RetiredAtUtc = NULL,
            RetiredReason = NULL,
            Notes = COALESCE(Notes + NCHAR(13) + NCHAR(10), N'')
                    + N'[Resurrected ' + CONVERT(NVARCHAR(33), @now, 127)
                    + N' by ' + @provider + N' on re-discovery; was retired: '
                    + COALESCE(RetiredReason, N'(no reason)') + N']',
            Email = COALESCE(Email, @cleanEmail),
            LinkedinUrl = COALESCE(LinkedinUrl, @cleanLinkedinUrl),
            Phone = COALESCE(Phone, @cleanPhone),
            Corroborations = Corroborations + 1,
            EmailSource =
                CASE
                    WHEN Email IS NULL AND @cleanEmail IS NOT NULL THEN COALESCE(EmailSource, @emailSourceForWrite)
                    ELSE EmailSource
                END,
            EmailConfidence =
                CASE
                    WHEN Email IS NULL AND @cleanEmail IS NOT NULL THEN COALESCE(EmailConfidence, @emailConfidenceForWrite)
                    ELSE EmailConfidence
                END,
            LastSeenAtUtc = @now,
            UpdatedAtUtc = @now
        WHERE Id = @retiredId
          AND RetiredAtUtc IS NOT NULL;

        IF @@ROWCOUNT = 1
        BEGIN
            SET @personId = @retiredId;
            COMMIT TRAN;
            RETURN;
        END;
    END;

    IF @orgId IS NULL
    BEGIN
        THROW 50001, 'orgId is required when creating a new IntelPerson because IntelPerson.SourceEnrichmentId requires a CanonicalOrgEnrichment parent.', 1;
    END;

    SELECT TOP (1) @sourceEnrichmentId = e.Id
    FROM opportunities.CanonicalOrgEnrichment AS e WITH (UPDLOCK, HOLDLOCK)
    WHERE e.CanonicalOrgId = @orgId
      AND e.ProviderName = @provider
    ORDER BY e.Id;

    IF @sourceEnrichmentId IS NULL
    BEGIN
        INSERT INTO opportunities.CanonicalOrgEnrichment
            (CanonicalOrgId, ProviderName, Status, Attempts, LastRefreshAtUtc, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@orgId, @provider, N'ok', 1, @now, @now, @now);

        SET @sourceEnrichmentId = CONVERT(BIGINT, SCOPE_IDENTITY());
    END;

    INSERT INTO opportunities.IntelPerson
        (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
         FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
         DisplayName, NormalizedName, Email, Phone, LinkedinUrl, Notes, EmailSource, EmailConfidence)
    VALUES
        (@provider, @sourceEnrichmentId, N'Medium', @naturalKey,
         @now, @now, @now, @now,
         LEFT(@cleanDisplayName, 200), @normalizedName, @cleanEmail, @cleanPhone, @cleanLinkedinUrl, @cleanNotes,
         CASE WHEN @cleanEmail IS NOT NULL THEN @emailSourceForWrite ELSE NULL END,
         CASE WHEN @cleanEmail IS NOT NULL THEN @emailConfidenceForWrite ELSE NULL END);

    SET @foundId = CONVERT(BIGINT, SCOPE_IDENTITY());
    SET @personId = @foundId;

    COMMIT TRAN;
END;
GO
