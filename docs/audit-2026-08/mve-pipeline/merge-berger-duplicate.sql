/*
  merge-berger-duplicate.sql   28 August 2026

  IntelPerson held the same human twice, both on pberger@mve-architects.com:

    13498  Peter Berger   jim-tracking-2026-06   High    Principal-in-Charge, OC
    13554  Pieter Berger  ca-ecosystem-2026-06   Medium  Associate Partner

  This is the tier-3/4 keying collision already measured on this table -- 211
  emails mapping to more than one key across 444 rows. A person first ingested
  without an email keys on name (+org), and a later ingest WITH the email keys
  on SHA1(email), so the same human lands twice.

  SURVIVOR: 13498. Higher confidence, and jim-tracking is human-sourced from
  someone who has met them; ca-ecosystem-2026-06 is a bulk pull. MVE's own
  about page lists neither spelling, so source quality decides it rather than
  a guess at "Peter" versus "Pieter".

  ⚠ THE DISCARDED TITLE IS NOT DISCARDED SILENTLY.
    The duplicate carried a DIFFERENT title -- "Associate Partner" against the
    survivor's "Principal-in-Charge, OC". Rather than attach a second,
    lower-confidence title to one person at one org, both the spelling and the
    title are preserved verbatim in RetiredReason so the merge is reversible
    and nothing is lost.

  ⚠ ASCII ONLY IN RetiredReason. An existing retirement on this table reads
    "Duplicate of 19417 <mojibake> stale ibigroup.com email variant" -- an
    em-dash that did not survive the write. Use plain hyphens.

  Safe to run: both rows had ZERO relations, touchpoints, engagements and
  research triggers, and one affiliation each, so nothing needs repointing.
  Verified before writing this.
*/

SET NOCOUNT ON;

DECLARE @survivor bigint = 13498;
DECLARE @dupe     bigint = 13554;
DECLARE @now      datetimeoffset = SYSDATETIMEOFFSET();
DECLARE @why      nvarchar(200) =
  N'Duplicate of 13498 (Peter Berger) - same email pberger@mve-architects.com, '
+ N'tier-3/4 name-key collision. Discarded variant: name "Pieter Berger", '
+ N'title "Associate Partner", source ca-ecosystem-2026-06, confidence Medium.';

-- Guard: refuse to run if anything now references the duplicate.
IF EXISTS (SELECT 1 FROM opportunities.IntelPersonRelation
            WHERE FromPersonId = @dupe OR ToPersonId = @dupe)
   OR EXISTS (SELECT 1 FROM opportunities.CrmTouchpoint WHERE IntelPersonId = @dupe)
   OR EXISTS (SELECT 1 FROM opportunities.CrmEngagements WHERE ContactIntelPersonId = @dupe)
   OR EXISTS (SELECT 1 FROM opportunities.BdPersonResearchTriggers WHERE IntelPersonId = @dupe)
BEGIN
    RAISERROR('Duplicate 13554 now has dependent rows - repoint them before merging.', 16, 1);
    RETURN;
END

UPDATE opportunities.IntelPersonAffiliation
   SET RetiredAtUtc = @now,
       RetiredReason = @why,
       IsCurrent = 0
 WHERE IntelPersonId = @dupe AND RetiredAtUtc IS NULL;

UPDATE opportunities.IntelPerson
   SET RetiredAtUtc = @now,
       RetiredReason = @why
 WHERE Id = @dupe AND RetiredAtUtc IS NULL;

SELECT Result   = 'merged',
       Survivor = (SELECT DisplayName FROM opportunities.IntelPerson WHERE Id = @survivor),
       SurvivorActiveAffiliations =
           (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation
             WHERE IntelPersonId = @survivor AND RetiredAtUtc IS NULL),
       ActivePeopleOnThatEmail =
           (SELECT COUNT(*) FROM opportunities.IntelPerson
             WHERE Email = 'pberger@mve-architects.com' AND RetiredAtUtc IS NULL);
GO
