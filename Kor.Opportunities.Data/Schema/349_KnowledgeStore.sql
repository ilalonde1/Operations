/*
    Kor.OpportunitiesDb migration 349.

    The BD knowledge store (2026-10-14). Additive only. KOR's own copy of the
    source / passage / claim shape JoeBrain uses, in KOR's database, for the BD
    Director. Nothing here references JoeBrain's store.

    1) knowledge.Source   — one row per ingested document, pinned by SHA-256 of
       the original file, with the licence it is held under. The original file
       is kept outside git (Ian's Books folder); the text lives here.
    2) knowledge.Passage  — the document split into cited chunks, in order, with
       a Ref (chapter / page) and an optional 384-dimension float32 embedding
       (bge-small-en-v1.5, 1536 bytes) for local meaning search. No service.
    3) knowledge.Claim    — a statement the Director may cite, tied to the one
       passage it rests on. Retired, never deleted. Scoring lives in a later
       migration.

    Written by the knowledge ingestion CLI (brief 9d); read by the BD Director
    window and the Director brief. Run in SSMS against KorOpportunitiesDb.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'knowledge')
    EXEC(N'CREATE SCHEMA knowledge AUTHORIZATION dbo;');
GO

IF OBJECT_ID(N'knowledge.Source', N'U') IS NULL
BEGIN
    CREATE TABLE knowledge.Source (
        Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Knowledge_Source PRIMARY KEY CLUSTERED,
        Kind            NVARCHAR(20)    NOT NULL CONSTRAINT CK_Knowledge_Source_Kind CHECK (Kind IN (N'Book', N'Document', N'Note')),
        Title           NVARCHAR(300)   NOT NULL,
        Author          NVARCHAR(300)   NULL,
        [Year]          SMALLINT        NULL,
        Origin          NVARCHAR(400)   NOT NULL,   -- the original file name, as Ian named it
        Sha256          CHAR(64)        NOT NULL,   -- of the original file, upper-case hex
        Licence         NVARCHAR(40)    NOT NULL CONSTRAINT CK_Knowledge_Source_Licence CHECK (Licence IN (
                            N'OwnedCopyPrivateStudy', N'PublicDomain', N'CcBy', N'CcBySa', N'CcByNcSa', N'CcByNcNd', N'Own', N'CiteOnly')),
        EmbeddingModel  NVARCHAR(40)    NULL,       -- NULL until the passages are embedded
        Passages        INT             NOT NULL CONSTRAINT DF_Knowledge_Source_Passages DEFAULT 0,
        Tokens          INT             NOT NULL CONSTRAINT DF_Knowledge_Source_Tokens DEFAULT 0,
        IngestedAtUtc   DATETIMEOFFSET  NOT NULL CONSTRAINT DF_Knowledge_Source_IngestedAt DEFAULT sysdatetimeoffset(),
        IngestedBy      NVARCHAR(150)   NOT NULL,
        Notes           NVARCHAR(1000)  NULL,
        RetiredAtUtc    DATETIMEOFFSET  NULL,
        RetiredReason   NVARCHAR(200)   NULL,
        CONSTRAINT UX_Knowledge_Source_Sha256 UNIQUE (Sha256)
    );
END
GO

IF OBJECT_ID(N'knowledge.Passage', N'U') IS NULL
BEGIN
    CREATE TABLE knowledge.Passage (
        Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Knowledge_Passage PRIMARY KEY CLUSTERED,
        SourceId        BIGINT          NOT NULL CONSTRAINT FK_Knowledge_Passage_Source REFERENCES knowledge.Source(Id),
        Ordinal         INT             NOT NULL,   -- reading order within the source, from 1
        Ref             NVARCHAR(200)   NULL,       -- chapter, section or page as the reader found it
        [Text]          NVARCHAR(MAX)   NOT NULL,
        Tokens          INT             NOT NULL,
        Embedding       VARBINARY(1536) NULL,       -- 384 x float32, little-endian; NULL until embedded
        CONSTRAINT UX_Knowledge_Passage_Source_Ordinal UNIQUE (SourceId, Ordinal)
    );
END
GO

IF OBJECT_ID(N'knowledge.Claim', N'U') IS NULL
BEGIN
    CREATE TABLE knowledge.Claim (
        Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Knowledge_Claim PRIMARY KEY CLUSTERED,
        SourceId        BIGINT          NOT NULL CONSTRAINT FK_Knowledge_Claim_Source REFERENCES knowledge.Source(Id),
        PassageId       BIGINT          NOT NULL CONSTRAINT FK_Knowledge_Claim_Passage REFERENCES knowledge.Passage(Id),
        Kind            NVARCHAR(20)    NOT NULL CONSTRAINT CK_Knowledge_Claim_Kind CHECK (Kind IN (N'Rule', N'Fact', N'Definition', N'Heuristic')),
        Statement       NVARCHAR(1000)  NOT NULL,   -- one sentence, in the Director's words, resting on the passage
        Confidence      DECIMAL(4,3)    NOT NULL CONSTRAINT CK_Knowledge_Claim_Confidence CHECK (Confidence BETWEEN 0 AND 1),
        ExtractedBy     NVARCHAR(100)   NOT NULL,   -- a model id or a person
        ExtractedAtUtc  DATETIMEOFFSET  NOT NULL CONSTRAINT DF_Knowledge_Claim_ExtractedAt DEFAULT sysdatetimeoffset(),
        RetiredAtUtc    DATETIMEOFFSET  NULL,
        RetiredReason   NVARCHAR(200)   NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Knowledge_Claim_Source' AND object_id = OBJECT_ID(N'knowledge.Claim'))
    CREATE INDEX IX_Knowledge_Claim_Source ON knowledge.Claim (SourceId) INCLUDE (Kind, RetiredAtUtc);
GO
