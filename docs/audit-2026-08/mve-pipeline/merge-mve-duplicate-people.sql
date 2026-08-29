/*
  merge-mve-duplicate-people.sql   28 August 2026

  FIVE of MVE's ~13 people in IntelPerson are duplicated. Found while checking
  one reported pair; the rest surfaced only when the query stopped being
  truncated with `head`.

    Pieter Berger     13498 + 13554 + 19252   (THREE rows)
    Daniel Gura       13539 + 19250
    Carl McLarand     13693 + 19249
    Matthew McLarand  13493 + 19248
    Chase Ronge       13491 + 19251

  This is the tier-3/4 keying collision already measured on this table: a
  person first ingested WITHOUT an email keys on name(+org); a later ingest
  WITH the email keys on SHA1(email); the same human lands twice.

  SURVIVOR RULE, applied consistently:
    1. the row carrying dependent rows (touchpoints, engagements) wins -- moving
       them is riskier than keeping them;
    2. else the row with more affiliations;
    3. else High confidence over Medium.
  Everything discarded is preserved verbatim in RetiredReason, so each merge is
  reversible and no title or spelling is lost.

  ⛔ FirmNarrative IS AUTHORITATIVE FOR NAMES, NOT FOR TITLES.
     It gives Pieter Berger "Principal + Director of Design" -- the SAME title
     it gives Matthew McLarand. Its title extraction is offset by a row. So the
     spelling "Pieter" is taken from it (corroborated by a second, independent
     source) while its titles are not.

  ⚠ NAME CORRECTION: 13498 was ingested as "Peter Berger" from jim-tracking.
     TWO independent sources spell it "Pieter" (19252 FirmNarrative High, 13554
     ca-ecosystem). The survivor keeps its id and dependents but takes the
     better-evidenced spelling.

  ⚠ NOT FIXED HERE: "Chase Rong?" is mojibake in BOTH rows -- an accented
     character that did not survive an earlier write -- and 19251's EMAIL is
     corrupt too (crong?@mve-architects.com). Retiring 19251 removes the bad
     email from the active set. The surviving DISPLAY NAME is still mojibake and
     needs a separate, encoding-aware fix; writing it from here risks repeating
     the same corruption. Flagged, not papered over.

  ⚠ ASCII ONLY in RetiredReason -- an existing retirement on this table already
    carries a mangled em-dash.
*/

SET NOCOUNT ON;

DECLARE @now datetimeoffset = SYSDATETIMEOFFSET();

DECLARE @m TABLE (Survivor bigint, Dupe bigint, Why nvarchar(200));
INSERT INTO @m (Survivor, Dupe, Why) VALUES
 (13498, 13554, N'Dup of 13498 Pieter Berger, pberger@mve-architects.com. Discarded: name "Pieter Berger", title "Associate Partner", src ca-ecosystem-2026-06, conf Medium.'),
 (13498, 19252, N'Dup of 13498 Pieter Berger, pberger@mve-architects.com. Discarded: title "Principal + Director of Design", src FirmNarrative, conf High - same title it gives M. McLarand, so unreliable.'),
 (13539, 19250, N'Dup of 13539 Daniel Gura, dgura@mve-architects.com. 13539 kept: carries the 2026-08-27 call touchpoint. Discarded: title "Director of Business Development", src FirmNarrative.'),
 (19249, 13693, N'Dup of 19249 Carl McLarand, cmclarand@mve-architects.com. 19249 kept: High conf, current title. Discarded: "Chairman & CEO", src CaArchitectResearch, conf Medium - superseded by Emeritus.'),
 (13493, 19248, N'Dup of 13493 Matthew McLarand, mmclarand@mve-architects.com. 13493 kept: two affiliations vs one. Discarded: title "President + Director of Design", src FirmNarrative, conf High.'),
 (13491, 19251, N'Dup of 13491 Chase Ronge, MVE San Diego. 13491 kept: two affiliations and a VALID email. Discarded row had a CORRUPT email (mojibake local part), src FirmNarrative.');

-- Guard: nothing may be silently orphaned.
IF EXISTS (SELECT 1 FROM @m m
            WHERE EXISTS (SELECT 1 FROM opportunities.IntelPersonRelation r
                           WHERE r.FromPersonId = m.Dupe OR r.ToPersonId = m.Dupe)
               OR EXISTS (SELECT 1 FROM opportunities.CrmTouchpoint t
                           WHERE t.IntelPersonId = m.Dupe)
               OR EXISTS (SELECT 1 FROM opportunities.CrmEngagements e
                           WHERE e.ContactIntelPersonId = m.Dupe)
               OR EXISTS (SELECT 1 FROM opportunities.BdPersonResearchTriggers b
                           WHERE b.IntelPersonId = m.Dupe))
BEGIN
    RAISERROR('A duplicate now carries dependent rows - repoint them before merging.', 16, 1);
    RETURN;
END

UPDATE a
   SET a.RetiredAtUtc = @now, a.RetiredReason = m.Why, a.IsCurrent = 0
  FROM opportunities.IntelPersonAffiliation a
  JOIN @m m ON m.Dupe = a.IntelPersonId
 WHERE a.RetiredAtUtc IS NULL;

UPDATE p
   SET p.RetiredAtUtc = @now, p.RetiredReason = m.Why
  FROM opportunities.IntelPerson p
  JOIN @m m ON m.Dupe = p.Id
 WHERE p.RetiredAtUtc IS NULL;

-- Spelling correction on the Berger survivor, evidenced by two sources.
UPDATE opportunities.IntelPerson
   SET DisplayName = N'Pieter Berger'
 WHERE Id = 13498 AND DisplayName = N'Peter Berger';

SELECT Email,
       ActiveRows = COUNT(*),
       Survivors  = MAX(DisplayName)
  FROM opportunities.IntelPerson
 WHERE Email LIKE '%@mve-architects.com' AND RetiredAtUtc IS NULL
 GROUP BY Email
 ORDER BY Email;
GO
