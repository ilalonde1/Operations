USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 297: keep the previous paragraph when a narrative is rewritten.

  Why. On 2026-09-03 canonical 74300 ("Continuum") held two unrelated companies
  after a bad --merge-dba merge. A FirmNarrative refresh researched the wrong one
  and IntelPersistenceService.MergeNarrativeAsync replaced ParagraphText IN PLACE
  on the same NaturalKey (SHA1 of CanonicalOrgId + NarrativeType). Rows 8730/8731
  kept their 2026-06-13 CreatedAtUtc and simply became a different company's text.

  SqlEnrichmentTrackingStore.RetireSupersededIntelAsync retires affiliations,
  signals, actions, work and risks but deliberately skips IntelNarrative because
  it "upserts cleanly" -- true, and precisely why there was nothing to recover.
  The only copy of the destroyed text was in a nightly backup, and those are
  Veeam VSS (device_type 7), not restorable as a file.

  This table is the undo. One row per superseded paragraph, written by the same
  MERGE that overwrites it, so history cannot drift from the live row.

  NOT a fix for wrong data -- that is ResearchIdentityGate's job. This makes a bad
  write RECOVERABLE and, just as important, makes "what did this say before?"
  answerable at all.
*/

IF OBJECT_ID(N'opportunities.IntelNarrativeVersion', N'U') IS NULL
BEGIN
    CREATE TABLE opportunities.IntelNarrativeVersion (
        Id                  BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_IntelNarrativeVersion PRIMARY KEY,
        -- Not an FK: the live row can be hard-deleted by a dedup merge, and the
        -- history of what it said must outlive it. That is the whole point.
        IntelNarrativeId    BIGINT          NULL,
        CanonicalOrgId      BIGINT          NOT NULL,
        NarrativeType       NVARCHAR(50)    NOT NULL,
        ParagraphText       NVARCHAR(MAX)   NOT NULL,
        SourceProviderName  NVARCHAR(100)   NULL,
        SourceEnrichmentId  BIGINT          NULL,
        -- When this text STOPPED being current (i.e. when it was replaced).
        SupersededAtUtc     DATETIMEOFFSET  NOT NULL
            CONSTRAINT DF_IntelNarrativeVersion_Superseded DEFAULT sysdatetimeoffset(),
        -- Which enrichment run replaced it, so a bad run's damage can be listed
        -- and reverted as a set rather than one row at a time.
        ReplacedByEnrichmentId BIGINT       NULL
    );

    CREATE INDEX IX_IntelNarrativeVersion_OrgWhen
        ON opportunities.IntelNarrativeVersion (CanonicalOrgId, SupersededAtUtc DESC);

    CREATE INDEX IX_IntelNarrativeVersion_ReplacedBy
        ON opportunities.IntelNarrativeVersion (ReplacedByEnrichmentId)
        WHERE ReplacedByEnrichmentId IS NOT NULL;

    PRINT 'Migration 297: created opportunities.IntelNarrativeVersion.';
END
ELSE
BEGIN
    PRINT 'Migration 297: opportunities.IntelNarrativeVersion already exists; no change.';
END
GO
