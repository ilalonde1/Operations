/* Connection-scoped like every other migration — no USE.
   APPLIED to KOR-APP01 KorOpportunitiesDb 2026-07-11. */

/* =====================================================================
   281 — OpportunitySources.QuartzManaged: scheduling ownership as data.
   ---------------------------------------------------------------------
   The cron plane's Quartz-exclusion was a hand-maintained name list in one
   SQL string that drifted twice ('BdAlerts' vs the real 'BdAlertsMailbox';
   only AB of five MPI sources listed) — double-polling the mailbox and
   re-fetching ~20k MPI rows daily. Ownership is now a column: sources with
   QuartzManaged = 1 run ONLY on their Quartz jobs; the cron scheduler skips
   them without any string literal to drift. A source promoted to a Quartz
   job in future gets its bit set here (or by its bootstrap/seed), not a
   code edit.
   ===================================================================== */

IF COL_LENGTH('opportunities.OpportunitySources', 'QuartzManaged') IS NULL
BEGIN
    ALTER TABLE opportunities.OpportunitySources
        ADD QuartzManaged bit NOT NULL
            CONSTRAINT DF_OpportunitySources_QuartzManaged DEFAULT (0);
END;
GO

UPDATE opportunities.OpportunitySources
SET QuartzManaged = 1
WHERE Name IN (N'CanadaBuys', N'CanadaBuysNew', N'SamGov', N'BdAlertsMailbox',
               N'AB_MajorProjectsInventory', N'BC_MajorProjectsInventory',
               N'CA_SocrataSF', N'CA_SocrataSanDiego', N'CA_SanJoseCkan', N'CA_CEQAnet');
