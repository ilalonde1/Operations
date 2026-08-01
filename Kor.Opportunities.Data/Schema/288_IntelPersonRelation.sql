/*
  288_IntelPersonRelation.sql  (2026-07-17)

  CRM Neural Gap Register G3: person-to-person edges. The highest-value BD
  knowledge — "Barry introduced Elliot", "Omar met Terry at the Ledcor client
  event" — lived only as prose in CanonicalOrg.Notes. This table makes it a
  walkable graph: "who can introduce us to X?" becomes a SELECT.

  Design:
  - Both endpoints are IntelPerson rows. KOR's own BD principals become
    IntelPerson nodes too (SourceProviderName 'KorStaff', email-keyed per the
    255 identity contract, affiliated to KOR Structural org 38918) — the graph
    is about people, and KOR people are people. The app-side BdStaff directory
    remains the staff identity for routing; these rows are the GRAPH identity.
  - RelationType is a closed vocabulary (CHECK): IntroducedBy | MetAt |
    ReportsTo | Colleague | WorkedWith. Direction matters for IntroducedBy
    (From introduced To to KOR) and ReportsTo (From reports to To); the
    others are read as symmetric.
  - NaturalKey = SHA1(FromId|ToId|Type|normalized-context) so re-running a
    decompose pass upserts instead of duplicating.
  - Retire, never delete (RetiredAtUtc), matching every other intel table.
*/

IF OBJECT_ID(N'opportunities.IntelPersonRelation', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelPersonRelation (
        Id                 BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NaturalKey         CHAR(40)        NOT NULL,
        FromPersonId       BIGINT          NOT NULL
            CONSTRAINT FK_IntelPersonRelation_From REFERENCES opportunities.IntelPerson (Id),
        ToPersonId         BIGINT          NOT NULL
            CONSTRAINT FK_IntelPersonRelation_To REFERENCES opportunities.IntelPerson (Id),
        RelationType       NVARCHAR(20)    NOT NULL
            CONSTRAINT CK_IntelPersonRelation_Type CHECK (RelationType IN
                (N'IntroducedBy', N'MetAt', N'ReportsTo', N'Colleague', N'WorkedWith')),
        Context            NVARCHAR(400)   NULL,   -- "Ledcor client event 2026-06-24"
        EvidencedAtUtc     DATETIMEOFFSET  NULL,   -- when the relationship event happened
        SourceProviderName NVARCHAR(100)   NOT NULL,
        SourceRef          NVARCHAR(400)   NULL,   -- email subject / dossier path / url
        CreatedAtUtc       DATETIMEOFFSET  NOT NULL CONSTRAINT DF_IntelPersonRelation_Created DEFAULT sysdatetimeoffset(),
        UpdatedAtUtc       DATETIMEOFFSET  NOT NULL CONSTRAINT DF_IntelPersonRelation_Updated DEFAULT sysdatetimeoffset(),
        RetiredAtUtc       DATETIMEOFFSET  NULL,
        RetiredReason      NVARCHAR(200)   NULL,
        CONSTRAINT UQ_IntelPersonRelation_NaturalKey UNIQUE (NaturalKey),
        CONSTRAINT CK_IntelPersonRelation_NoSelfEdge CHECK (FromPersonId <> ToPersonId)
    );
    CREATE INDEX IX_IntelPersonRelation_From ON opportunities.IntelPersonRelation (FromPersonId) WHERE RetiredAtUtc IS NULL;
    CREATE INDEX IX_IntelPersonRelation_To   ON opportunities.IntelPersonRelation (ToPersonId)   WHERE RetiredAtUtc IS NULL;
END
GO

/* ---- KOR BD principals as graph nodes (email-keyed, idempotent) --------- */
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @now datetimeoffset = sysdatetimeoffset();
DECLARE @korOrg bigint = 38918;  -- KOR Structural Ltd. (canonical)

DECLARE @staff TABLE (Person nvarchar(100), NormName nvarchar(100), Email nvarchar(200), Title nvarchar(120), PKey char(40), PersonId bigint);
INSERT @staff (Person, NormName, Email, Title) VALUES
 (N'Omar Alcazar Pastrana', N'omaralcazarpastrana', N'omara@korstructural.com',      N'Senior Structural Engineer, Associate Principal (KOR)'),
 (N'Jim DesRoches',         N'jimdesroches',        N'jdesroches@korstructural.com', N'Principal, Senior Structural Engineer (KOR)'),
 (N'Islam Shabana',         N'islamshabana',        N'islams@korstructural.com',     N'Senior Structural Engineer (KOR)');

UPDATE @staff SET PKey = CONVERT(char(40), HASHBYTES('SHA1', CAST(LOWER(Email) AS varchar(8000))), 2);

DECLARE @enr TABLE (EnrId bigint);
INSERT opportunities.CanonicalOrgEnrichment (CanonicalOrgId, ProviderName, Status, Attempts, LastRefreshAtUtc, CreatedAtUtc, UpdatedAtUtc)
OUTPUT inserted.Id INTO @enr VALUES (@korOrg, N'KorStaff-288', N'ok', 1, @now, @now, @now);
DECLARE @enrId bigint = (SELECT TOP 1 EnrId FROM @enr);

MERGE opportunities.IntelPerson WITH (HOLDLOCK) AS T
USING (SELECT PKey, Person, NormName, Email FROM @staff) AS S ON T.NaturalKey = S.PKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
    FirstSeenAtUtc, LastSeenAtUtc, DisplayName, NormalizedName, Email, EmailSource, EmailConfidence)
   VALUES (N'KorStaff', @enrId, N'High', S.PKey, @now, @now, S.Person, S.NormName, S.Email, N'asis', 100);

UPDATE s SET s.PersonId = p.Id FROM @staff s JOIN opportunities.IntelPerson p ON p.NaturalKey = s.PKey;

MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T
USING (SELECT s.PersonId, s.Title,
              CONVERT(char(40), HASHBYTES('SHA1', CAST(CAST(s.PersonId AS varchar(20)) + '|' + CAST(@korOrg AS varchar(20)) + '|korstaff' AS varchar(8000))), 2) AS AffKey
       FROM @staff s) AS S ON T.NaturalKey = S.AffKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
    FirstSeenAtUtc, LastSeenAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
   VALUES (N'KorStaff', @enrId, N'High', S.AffKey, @now, @now, S.PersonId, @korOrg, S.Title, 1);

/* ---- Backfill the edges we hold from primary sources -------------------- */
-- External nodes (must already exist; fail-soft to NULL and skip if absent):
DECLARE @omar  bigint = (SELECT PersonId FROM @staff WHERE Email = N'omara@korstructural.com');
DECLARE @jim   bigint = (SELECT PersonId FROM @staff WHERE Email = N'jdesroches@korstructural.com');
DECLARE @barry bigint = (SELECT Id FROM opportunities.IntelPerson WHERE Email = N'barry.murphy@ledcor.com'  AND RetiredAtUtc IS NULL);
DECLARE @ell   bigint = (SELECT Id FROM opportunities.IntelPerson WHERE Email = N'elliot.wood@ledcor.com'   AND RetiredAtUtc IS NULL);
DECLARE @terry bigint = (SELECT Id FROM opportunities.IntelPerson WHERE Email = N'terry.gray@arcadis.com'   AND RetiredAtUtc IS NULL);

DECLARE @edges TABLE (FromId bigint, ToId bigint, RelType nvarchar(20), Context nvarchar(400), Ev datetimeoffset, SrcRef nvarchar(400));
IF @omar IS NOT NULL AND @barry IS NOT NULL INSERT @edges VALUES
  (@omar, @barry, N'MetAt', N'Ledcor client event, Vancouver', '2026-06-24', N'msg: Introduction to Ledcor Kelowna (2026-07-15)');
IF @barry IS NOT NULL AND @ell IS NOT NULL INSERT @edges VALUES
  (@barry, @ell, N'IntroducedBy', N'Barry forwarded Omar''s request to Elliot: "obviously you''re the man!"', '2026-07-16', N'msg: Fw: Introduction to Ledcor Kelowna (2026-07-16)');
IF @omar IS NOT NULL AND @terry IS NOT NULL INSERT @edges VALUES
  (@omar, @terry, N'MetAt', N'Ledcor client event, Vancouver', '2026-06-24', N'msg: RE KOR/Arcadis Edmonton and Mexico Collaboration (2026-07-14)');
IF @jim IS NOT NULL AND @ell IS NOT NULL INSERT @edges VALUES
  (@jim, @ell, N'MetAt', N'First sit-down booked: Ledcor Kelowna office (Landmark 4), Mon 2026-07-20 10:00', '2026-07-20', N'msg: RE: Introduction to Ledcor Kelowna (2026-07-17)');

MERGE opportunities.IntelPersonRelation WITH (HOLDLOCK) AS T
USING (SELECT FromId, ToId, RelType, Context, Ev, SrcRef,
              CONVERT(char(40), HASHBYTES('SHA1', CAST(
                CAST(FromId AS varchar(20)) + '|' + CAST(ToId AS varchar(20)) + '|' + RelType + '|' +
                LOWER(REPLACE(ISNULL(Context, N''), ' ', '')) AS varchar(8000))), 2) AS NK
       FROM @edges) AS S ON T.NaturalKey = S.NK
WHEN MATCHED THEN UPDATE SET UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
   (NaturalKey, FromPersonId, ToPersonId, RelationType, Context, EvidencedAtUtc, SourceProviderName, SourceRef)
   VALUES (S.NK, S.FromId, S.ToId, S.RelType, S.Context, S.Ev, N'BrainDecompose-2026-07-17', S.SrcRef);

SELECT KorStaffNodes = (SELECT COUNT(*) FROM @staff WHERE PersonId IS NOT NULL),
       EdgesBanked   = (SELECT COUNT(*) FROM opportunities.IntelPersonRelation WHERE RetiredAtUtc IS NULL);
GO
