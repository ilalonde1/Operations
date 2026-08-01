/*
    Kor.OpportunitiesDb migration 283.

    Queue-guard for the Phase-2 live-opp detail enricher. DetailEnrichedAtUtc is
    stamped once an opportunity's detail page has been visited (success OR
    no-data), so the enrichment job selects `WHERE DetailEnrichedAtUtc IS NULL`
    and an opp is attempted exactly once — never re-queued forever. This is the
    fix for the class of bug seen in BcBidPlanTakerEnrichmentJob, where opps with
    nothing to extract were re-selected every run and starved the batch.

    Additive, idempotent, nullable — zero impact on existing rows (all start NULL
    = "not yet enriched", which is correct).
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH(N'opportunities.Opportunities', N'DetailEnrichedAtUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.Opportunities ADD DetailEnrichedAtUtc DATETIMEOFFSET NULL;
END;
GO

-- Filtered index: the enrichment job's hot query is "live opps not yet enriched".
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'opportunities.Opportunities')
                 AND name = N'IX_Opp_DetailEnrichPending')
BEGIN
    CREATE INDEX IX_Opp_DetailEnrichPending
        ON opportunities.Opportunities (Id)
        WHERE DetailEnrichedAtUtc IS NULL;
END;
GO

PRINT 'Migration 283 complete.';
GO
