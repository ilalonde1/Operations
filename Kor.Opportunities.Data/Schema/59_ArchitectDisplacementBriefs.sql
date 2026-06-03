/*
    Kor.OpportunitiesDb migration 59.
    Architect Displacement Briefs - the synthesis layer that ties together
    structural-partner-map, competitor-research, architect-pipelines,
    prime-decisionmakers, and capability-corpus into one per-architect
    BD playbook.

    One row per ArchitectCanonicalOrgId. Brief content is stored as JSON
    so the schema can evolve without migrations - the WPF dossier renders
    structured sections from the JSON, and the importer just upserts.

    Surfaced on the Org Dossier panel for any architect with a brief.
*/
IF OBJECT_ID(N'opportunities.ArchitectDisplacementBriefs', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.ArchitectDisplacementBriefs (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ArchitectCanonicalOrgId  BIGINT           NOT NULL,
        Market                   NVARCHAR(80)     NULL,
        KorPriority              NVARCHAR(20)     NULL,   -- high | medium | low
        ConfidenceScore          DECIMAL(4,3)     NULL,   -- 0.000 - 1.000
        BriefJson                NVARCHAR(MAX)    NOT NULL,
        GeneratedAtUtc           DATETIMEOFFSET   NOT NULL,
        UpdatedAtUtc             DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        CONSTRAINT FK_ArchitectDisplacementBriefs_CanonicalOrg
            FOREIGN KEY (ArchitectCanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id)
            ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_ArchitectDisplacementBriefs_Architect
        ON opportunities.ArchitectDisplacementBriefs (ArchitectCanonicalOrgId);

    CREATE INDEX IX_ArchitectDisplacementBriefs_Priority
        ON opportunities.ArchitectDisplacementBriefs (KorPriority, ConfidenceScore DESC)
        WHERE KorPriority IS NOT NULL;
END;
GO

PRINT 'Migration 59 complete.';
GO
