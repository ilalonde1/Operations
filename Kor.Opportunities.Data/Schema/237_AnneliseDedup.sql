USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 237: person dedup. "Annelise Veen" (13804, Hunter) and "Annelise
   van der Veen" (13816, Apollo + phone) are the same Purpose Driven person.
   Keep 13816 (fuller surname + email + direct phone); retire 13804 + its
   duplicate affiliation. */
BEGIN TRAN;
UPDATE opportunities.IntelPersonAffiliation SET RetiredAtUtc=sysdatetimeoffset(), RetiredReason=N'Dup of Annelise van der Veen (13816) (migration 237)', IsCurrent=0 WHERE IntelPersonId=13804 AND RetiredAtUtc IS NULL;
UPDATE opportunities.IntelPerson SET RetiredAtUtc=sysdatetimeoffset(), RetiredReason=N'Duplicate of Annelise van der Veen (13816) (migration 237)', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=13804;
PRINT 'Migration 237: Annelise dedup (13804 -> 13816).';
COMMIT TRAN;
GO
