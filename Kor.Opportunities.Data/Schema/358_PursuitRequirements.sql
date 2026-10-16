IF OBJECT_ID(N'opportunities.PursuitRequirement', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.PursuitRequirement
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PursuitRequirement PRIMARY KEY,
        EngagementId BIGINT NOT NULL,
        Ordinal INT NOT NULL,
        Requirement NVARCHAR(1000) NOT NULL,
        SourceRef NVARCHAR(200) NOT NULL,
        CheckKind NVARCHAR(20) NOT NULL,
        CheckParameter NVARCHAR(400) NULL,
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_PursuitRequirement_Status DEFAULT N'Open',
        StatusNote NVARCHAR(500) NULL,
        CheckedBy NVARCHAR(150) NULL,
        CheckedAtUtc DATETIMEOFFSET(7) NULL,
        CreatedBy NVARCHAR(150) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_PursuitRequirement_Created DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT FK_PursuitRequirement_Engagement FOREIGN KEY (EngagementId)
            REFERENCES opportunities.CrmEngagements(Id),
        CONSTRAINT CK_PursuitRequirement_Kind CHECK (CheckKind IN (N'Manual', N'PhrasePresent', N'PageLimit', N'FileName')),
        CONSTRAINT CK_PursuitRequirement_Status CHECK (Status IN (N'Open', N'Met', N'NotMet', N'NotApplicable'))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'opportunities.PursuitRequirement')
               AND name = N'IX_PursuitRequirement_Engagement_Ordinal')
    CREATE INDEX IX_PursuitRequirement_Engagement_Ordinal ON opportunities.PursuitRequirement(EngagementId, Ordinal);
GO
