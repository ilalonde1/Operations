USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO
/* Migration 249: post-Deltek-resolution annotations (2026-06-20).
   - co.lab Architecture (29920): active Deltek client CL00465 but no project,
     contact, or web presence; identity unverifiable (Ian confirmed). Flag +
     suppress enrichment rather than retire (it is an active Deltek client).
   - OctoberNine Capital (192) <-> Frame Properties Ltd. (41): distinct Deltek
     clients but same principals (contacts use @frame.properties); cross-link.
   Idempotent: each UPDATE re-runs safely. */
BEGIN TRAN;

-- co.lab Architecture: flag dead-end + suppress enrichment
UPDATE opportunities.CanonicalOrg
SET Notes = N'Deltek client CL00465 (active) but with no project, contact, or web presence as of 2026-06-20; identity unverifiable - likely a lapsed or erroneous setup. Confirm in Deltek before any pursuit.',
    EnrichmentSuppressedAtUtc = COALESCE(EnrichmentSuppressedAtUtc, sysdatetimeoffset()),
    EnrichmentSuppressedReason = N'Unverifiable phantom client; no Deltek project/contact, no web presence (confirmed 2026-06-20).',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 29920;

-- OctoberNine Capital -> note its Frame Properties operating brand / related client
UPDATE opportunities.CanonicalOrg
SET Notes = LTRIM(ISNULL(Notes, N'') + N' Operating principals use the Frame Properties brand (@frame.properties); related Deltek client: Frame Properties Ltd. (#41).'),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 192 AND (Notes IS NULL OR Notes NOT LIKE N'%#41%');

-- Frame Properties Ltd. -> note related OctoberNine Capital client
UPDATE opportunities.CanonicalOrg
SET Notes = LTRIM(ISNULL(Notes, N'') + N' Related Deltek client: OctoberNine Capital Inc. (#192) - same principals (Frame Properties).'),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = 41 AND (Notes IS NULL OR Notes NOT LIKE N'%#192%');

PRINT 'Migration 249: co.lab flagged/suppressed + OctoberNine<->Frame cross-link.';
COMMIT TRAN;
GO
