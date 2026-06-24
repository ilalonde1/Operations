USE [KorOpportunitiesDb];
GO

/* =====================================================================
   265 — Shared IntelPerson resolver/create proc.
   ---------------------------------------------------------------------
   Single SQL source of truth for person identity resolution across SQL and
   Apollo-style ingest paths. Resolves by stable columns first (email,
   LinkedIn, then normalized name + active org affiliation) before inserting
   with the IntelNaturalKey.ComputePerson identity-anchor contract:
     1. trimmed/lower email
     2. trimmed/lower LinkedinUrl
     3. normalize(DisplayName) + '|org:' + orgId
     4. normalize(DisplayName)

   normalize() and HASHBYTES convention intentionally mirror migration 162 /
   CanonicalOrgResolver.NormalizeName:
     lower + strip space . , ' - & / ( ) +
     CONVERT(char(40), HASHBYTES('SHA1', CAST(key AS varchar(8000))), 2)
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
