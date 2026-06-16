USE [KorOpportunitiesDb];
GO

/* =====================================================================
   144 — Email provenance on IntelPerson (so inferred never masquerades as verified).
   ---------------------------------------------------------------------
   The pollution in migration 143 happened because there was no way to tell a
   verified personal email from an LLM-inferred guess — both just sat in Email.
   This adds:
     EmailSource     — where the address came from: 'asis' (pre-existing clean),
                       'Hunter' (Hunter personal hit), 'HunterPattern' (constructed
                       from Hunter's domain pattern), 'PatternInferred' (constructed
                       from our own known-email pattern), 'Verified' (MX/SMTP confirmed).
     EmailConfidence — 0..100 (Hunter's score, or our inference confidence).
     EmailCheckedAtUtc — when the address was last sourced/verified.
   Existing non-null emails (post-143 cleanup) are backfilled as 'asis' @ 80.
   Idempotent: column adds are COL_LENGTH-guarded; backfill only fills NULL source.
   ===================================================================== */

IF COL_LENGTH('opportunities.IntelPerson', 'EmailSource') IS NULL
    ALTER TABLE opportunities.IntelPerson ADD EmailSource nvarchar(20) NULL;
GO
IF COL_LENGTH('opportunities.IntelPerson', 'EmailConfidence') IS NULL
    ALTER TABLE opportunities.IntelPerson ADD EmailConfidence tinyint NULL;
GO
IF COL_LENGTH('opportunities.IntelPerson', 'EmailCheckedAtUtc') IS NULL
    ALTER TABLE opportunities.IntelPerson ADD EmailCheckedAtUtc datetimeoffset NULL;
GO

UPDATE opportunities.IntelPerson
SET EmailSource = N'asis', EmailConfidence = 80
WHERE NULLIF(LTRIM(RTRIM(Email)), '') IS NOT NULL AND EmailSource IS NULL;
GO
