USE [KorOpportunitiesDb];
GO

/* =====================================================================
   163 — Contact data-quality cleanup (surfaced during the 2026-06-16
   Prime-contact honing pass).
   ---------------------------------------------------------------------
   A. Retire 4 mislinked affiliations (people attached to the wrong org):
      - Paul Fast (Fast + Epp founder) wrongly linked to hcma; his correct
        Fast + Epp affiliation is left intact.
      - 3 project-side people (JWest Foundation / Urban One Builders /
        JWest Dev Corp) wrongly linked to architect Acton Ostry.
   B. Clean 32 IntelPerson.Email values polluted with provenance prose
      (e.g. 'x@y.com — PATTERN: ZoomInfo ...' or masked 's***@firm.ca').
      Extract the real address; NULL masked/invalid ones; preserve the
      original string in Notes so confidence/provenance isn't lost.

   NOTE (root cause, deferred): the polluted emails came from research-
   agent enrichment storing the model's email-with-rationale verbatim.
   Upstream fix = sanitize email at ingest in the enrichment writer.
   ===================================================================== */

SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

/* ---- A. Retire mislinked affiliations ---- */
UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = @now,
    RetiredReason = N'Mislinked affiliation — person belongs to a different org (migration 163)',
    UpdatedAtUtc = @now
WHERE Id IN (19619 /*Paul Fast -> hcma*/, 18397 /*Hayden Kremer -> Acton Ostry*/,
             18399 /*Stefan du Toit -> Acton Ostry*/, 18401 /*David Porte -> Acton Ostry*/)
  AND RetiredAtUtc IS NULL;

/* ---- B. Clean prose-polluted emails ---- */
;WITH d AS (
  SELECT Id, Email,
    LTRIM(RTRIM(LEFT(Email,
      CASE WHEN CHARINDEX(' ', Email) > 0 THEN CHARINDEX(' ', Email) - 1 ELSE LEN(Email) END))) AS Clean
  FROM opportunities.IntelPerson
  WHERE RetiredAtUtc IS NULL AND Email IS NOT NULL
    AND (Email LIKE '% %' OR Email LIKE '%—%' OR Email LIKE '%:%')
)
UPDATE t SET
  Notes = LEFT(COALESCE(t.Notes + N' | ', N'') + N'[orig-email] ' + t.Email, 4000),
  Email = CASE WHEN d.Clean LIKE '%*%' OR d.Clean NOT LIKE '%_@_%._%' THEN NULL ELSE d.Clean END,
  -- masked/invalid -> no usable email; drop the email-confidence signal too
  EmailSource = CASE WHEN d.Clean LIKE '%*%' OR d.Clean NOT LIKE '%_@_%._%' THEN NULL ELSE t.EmailSource END,
  EmailConfidence = CASE WHEN d.Clean LIKE '%*%' OR d.Clean NOT LIKE '%_@_%._%' THEN NULL ELSE t.EmailConfidence END,
  UpdatedAtUtc = @now
FROM opportunities.IntelPerson t
JOIN d ON d.Id = t.Id;
GO
