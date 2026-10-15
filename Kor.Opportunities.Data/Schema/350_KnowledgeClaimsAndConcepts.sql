/*
    KorOpportunitiesDb migration 350: cited claims, shared concepts and file imports.
    The ingestion CLI imports; the Director window reviews. Additive only, no data moves.
    Requires migration 349. Nothing here removes or rewords a claim.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'knowledge.Concept', N'U') IS NULL
BEGIN
    CREATE TABLE knowledge.Concept (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Knowledge_Concept PRIMARY KEY CLUSTERED,
        Slug NVARCHAR(80) NOT NULL,
        Name NVARCHAR(150) NOT NULL,
        Summary NVARCHAR(500) NULL,
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_Knowledge_Concept_CreatedAt DEFAULT sysdatetimeoffset(),
        CreatedBy NVARCHAR(100) NOT NULL,
        CONSTRAINT UX_Knowledge_Concept_Slug UNIQUE (Slug)
    );
END
GO

IF OBJECT_ID(N'knowledge.ClaimConcept', N'U') IS NULL
BEGIN
    CREATE TABLE knowledge.ClaimConcept (
        ClaimId BIGINT NOT NULL CONSTRAINT FK_Knowledge_ClaimConcept_Claim REFERENCES knowledge.Claim(Id),
        ConceptId BIGINT NOT NULL CONSTRAINT FK_Knowledge_ClaimConcept_Concept REFERENCES knowledge.Concept(Id),
        Confidence DECIMAL(4,3) NOT NULL CONSTRAINT CK_Knowledge_ClaimConcept_Confidence CHECK (Confidence BETWEEN 0 AND 1),
        LinkedBy NVARCHAR(10) NOT NULL CONSTRAINT CK_Knowledge_ClaimConcept_LinkedBy CHECK (LinkedBy IN (N'Model', N'Human')),
        CONSTRAINT PK_Knowledge_ClaimConcept PRIMARY KEY (ClaimId, ConceptId)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Knowledge_ClaimConcept_Concept' AND object_id = OBJECT_ID(N'knowledge.ClaimConcept'))
    CREATE INDEX IX_Knowledge_ClaimConcept_Concept ON knowledge.ClaimConcept (ConceptId);
GO

IF OBJECT_ID(N'knowledge.ClaimImport', N'U') IS NULL
BEGIN
    CREATE TABLE knowledge.ClaimImport (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Knowledge_ClaimImport PRIMARY KEY CLUSTERED,
        SourceId BIGINT NOT NULL CONSTRAINT FK_Knowledge_ClaimImport_Source REFERENCES knowledge.Source(Id),
        FileName NVARCHAR(260) NOT NULL,
        FileSha256 CHAR(64) NOT NULL,
        ImportedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_Knowledge_ClaimImport_ImportedAt DEFAULT sysdatetimeoffset(),
        ImportedBy NVARCHAR(100) NOT NULL,
        PassagesCovered INT NOT NULL,
        ClaimsWritten INT NOT NULL,
        ClaimsRejected INT NOT NULL,
        Forced BIT NOT NULL
    );
END
GO

IF COL_LENGTH(N'knowledge.Claim', N'ReviewedAtUtc') IS NULL
    ALTER TABLE knowledge.Claim ADD ReviewedAtUtc DATETIMEOFFSET NULL;
IF COL_LENGTH(N'knowledge.Claim', N'ReviewedBy') IS NULL
    ALTER TABLE knowledge.Claim ADD ReviewedBy NVARCHAR(150) NULL;
GO

IF COL_LENGTH(N'knowledge.Passage', N'ClaimsExtractedAtUtc') IS NULL
    ALTER TABLE knowledge.Passage ADD ClaimsExtractedAtUtc DATETIMEOFFSET NULL;
IF COL_LENGTH(N'knowledge.Passage', N'ClaimsExtractedBy') IS NULL
    ALTER TABLE knowledge.Passage ADD ClaimsExtractedBy NVARCHAR(100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Knowledge_Passage_Unextracted' AND object_id = OBJECT_ID(N'knowledge.Passage'))
    CREATE INDEX IX_Knowledge_Passage_Unextracted ON knowledge.Passage (SourceId, Ordinal) WHERE ClaimsExtractedAtUtc IS NULL;
GO
