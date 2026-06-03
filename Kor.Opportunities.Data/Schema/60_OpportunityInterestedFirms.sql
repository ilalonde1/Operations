/*
    Kor.OpportunitiesDb migration 60.

    Per-posting expressed-interest firm list. Capture the firms (architects,
    structural competitors, GCs, sub-consultants) that registered interest in
    a procurement notice via the source portal's "Expressed Interest" /
    "Plan-Takers" / "Bidder List" feature. This is the layer that converts
    "we have a feed of RFPs" into "we know who else is in this fight."

    Per-opportunity rows: one entry per interested firm, idempotent on
    (OpportunityId, RawFirmName). Resolved CanonicalOrgId is populated by
    CanonicalOrgResolver during ingestion / backfill so the firm rows light
    up in the architect/competitor dossiers as "Recent expressed interest:
    Project X (closes YYYY-MM-DD)".

    Schema-agnostic across sources: APC + BC Bid + MERX + Bonfire can all
    write rows here as their detail-page scrapers extend.
*/
IF OBJECT_ID(N'opportunities.OpportunityInterestedFirms', 'U') IS NULL
BEGIN
    CREATE TABLE opportunities.OpportunityInterestedFirms (
        Id                       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OpportunityId            BIGINT           NOT NULL,
        RawFirmName              NVARCHAR(400)    NOT NULL,
        ResolvedCanonicalOrgId   BIGINT           NULL,
        ResolvedKind             NVARCHAR(40)     NULL,
        SourcePortal             NVARCHAR(40)     NOT NULL, -- 'APC','BCBID','MERX','BONFIRE', etc.
        SourcePostingUrl         NVARCHAR(1000)   NULL,
        ExpressedAtUtc           DATETIMEOFFSET   NULL,     -- when interest was registered (if available)
        FirstSeenAtUtc           DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        LastSeenAtUtc            DATETIMEOFFSET   NOT NULL DEFAULT sysdatetimeoffset(),
        Notes                    NVARCHAR(1000)   NULL,
        RawJson                  NVARCHAR(MAX)    NULL,     -- preserve raw row in case the portal exposes more later

        CONSTRAINT FK_OpportunityInterestedFirms_Opportunity
            FOREIGN KEY (OpportunityId)
            REFERENCES opportunities.Opportunities(Id)
            ON DELETE CASCADE,

        CONSTRAINT FK_OpportunityInterestedFirms_CanonicalOrg
            FOREIGN KEY (ResolvedCanonicalOrgId)
            REFERENCES opportunities.CanonicalOrg(Id)
            -- NO CASCADE: if a canonical is merged, the resolver re-resolves on the
            -- next pass. Leaving NULL temporarily is fine.
    );

    -- Idempotent ingestion + backfill: same firm raised on same opp doesn't dup
    CREATE UNIQUE INDEX UX_OpportunityInterestedFirms_OppFirm
        ON opportunities.OpportunityInterestedFirms (OpportunityId, RawFirmName);

    -- "All recent expressed-interest by this canonical org" - powers the
    -- architect/competitor dossier section.
    CREATE INDEX IX_OpportunityInterestedFirms_CanonicalRecent
        ON opportunities.OpportunityInterestedFirms (ResolvedCanonicalOrgId, ExpressedAtUtc DESC)
        WHERE ResolvedCanonicalOrgId IS NOT NULL;

    -- "Show me all interested firms on this opportunity" - powers the
    -- Opportunity dossier panel section.
    CREATE INDEX IX_OpportunityInterestedFirms_OppRecent
        ON opportunities.OpportunityInterestedFirms (OpportunityId, ExpressedAtUtc DESC);
END;
GO

PRINT 'Migration 60 complete.';
GO
