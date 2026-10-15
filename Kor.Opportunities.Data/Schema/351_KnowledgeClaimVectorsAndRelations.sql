/*
    KorOpportunitiesDb migration 351: claim vectors and cited relations.
    The ingestion CLI writes vectors/relations. The MCP brief only reads knowledge.
    Additive, guarded, and dependent on migrations 349–350.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH(N'knowledge.Claim', N'Embedding') IS NULL
    ALTER TABLE knowledge.Claim ADD Embedding VARBINARY(1536) NULL;
IF COL_LENGTH(N'knowledge.Claim', N'EmbeddingModel') IS NULL
    ALTER TABLE knowledge.Claim ADD EmbeddingModel NVARCHAR(40) NULL;
GO

IF OBJECT_ID(N'knowledge.ClaimRelation', N'U') IS NULL
BEGIN
    CREATE TABLE knowledge.ClaimRelation (
        FromClaimId BIGINT NOT NULL CONSTRAINT FK_Knowledge_ClaimRelation_From REFERENCES knowledge.Claim(Id),
        ToClaimId BIGINT NOT NULL CONSTRAINT FK_Knowledge_ClaimRelation_To REFERENCES knowledge.Claim(Id),
        Kind NVARCHAR(12) NOT NULL CONSTRAINT CK_Knowledge_ClaimRelation_Kind CHECK (Kind IN (N'Supports', N'Contradicts', N'Refines')),
        Confidence DECIMAL(4,3) NOT NULL CONSTRAINT CK_Knowledge_ClaimRelation_Confidence CHECK (Confidence BETWEEN 0 AND 1),
        LinkedBy NVARCHAR(10) NOT NULL CONSTRAINT CK_Knowledge_ClaimRelation_LinkedBy CHECK (LinkedBy IN (N'Model', N'Human')),
        Note NVARCHAR(500) NULL,
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_Knowledge_ClaimRelation_CreatedAt DEFAULT sysdatetimeoffset(),
        CreatedBy NVARCHAR(100) NOT NULL,
        CONSTRAINT PK_Knowledge_ClaimRelation PRIMARY KEY (FromClaimId, ToClaimId, Kind),
        CONSTRAINT CK_Knowledge_ClaimRelation_Different CHECK (FromClaimId <> ToClaimId)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Knowledge_ClaimRelation_To' AND object_id = OBJECT_ID(N'knowledge.ClaimRelation'))
    CREATE INDEX IX_Knowledge_ClaimRelation_To ON knowledge.ClaimRelation (ToClaimId);
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'mcp_app')
BEGIN
    -- The MCP brief reads knowledge; it has no knowledge write path.
    GRANT SELECT ON SCHEMA::knowledge TO mcp_app;
END
GO
