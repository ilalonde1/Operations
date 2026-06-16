USE [KorOpportunitiesDb];
GO

/* =====================================================================
   143 — Clean the IntelPerson.Email field (de-pollute prior LLM email-finding).
   ---------------------------------------------------------------------
   A prior LLM contact-research pass jammed research commentary, "(unconfirmed —
   inferred)" markers, and even wrong-company addresses into the Email column, e.g.
   "aberon@urbanonebuilders.com — PATTERN: ZoomInfo lists a***@..." . The Email
   column's contract is: a single clean personal email, or NULL. This regularizes it:
     1) strip any trailing text from the first whitespace onward (drops the notes),
     2) NULL anything that still isn't a single valid-shaped email,
     3) NULL generic/non-personal mailboxes (info@/admin@/office@/...) — those are
        firm inboxes, not a person's contact, and pollute selector-contactability.
   Conservative: only touches rows that are demonstrably not a clean personal email.
   Idempotent.

   NOTE: the durable fix is the email-discovery rework (pattern-propagation with a
   separate EmailSource/EmailConfidence, Hunter.io + MX verification) — LLM never
   writes the email string. See docs/bd-enrichment-2026-06-15.md.
   ===================================================================== */

-- 1) Strip trailing editorial text (everything from the first whitespace onward).
UPDATE opportunities.IntelPerson
SET Email = LEFT(LTRIM(RTRIM(Email)), CHARINDEX(' ', LTRIM(RTRIM(Email)) + ' ') - 1)
WHERE Email LIKE '% %';
GO

-- 2) NULL anything that is no longer a single, valid-shaped email.
UPDATE opportunities.IntelPerson
SET Email = NULL
WHERE Email IS NOT NULL
  AND ( Email NOT LIKE '%_@_%.__%'   -- not even local@domain.tld shaped
     OR Email LIKE '%@%@%'           -- concatenated addresses
     OR Email LIKE '%(%' OR Email LIKE '%)%'
     OR Email LIKE '%,%' OR Email LIKE '%;%' );
GO

-- 3) NULL generic / non-personal mailboxes (firm inboxes, not a person's contact).
UPDATE opportunities.IntelPerson
SET Email = NULL
WHERE Email LIKE 'info@%'      OR Email LIKE 'admin@%'   OR Email LIKE 'office@%'
   OR Email LIKE 'contact@%'   OR Email LIKE 'reception@%' OR Email LIKE 'hello@%'
   OR Email LIKE 'sales@%'     OR Email LIKE 'careers@%'  OR Email LIKE 'general@%'
   OR Email LIKE 'enquiries@%' OR Email LIKE 'inquiries@%';
GO
