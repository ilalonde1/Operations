USE [KorOpportunitiesDb];
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/*
  Migration 224: clean the remaining project-linked JV-string org names.
  - Named construction JVs: keep as their own entity, rename to the real JV name,
    capture members in Notes (per policy: JV bids as one legal entity).
  - Split-leads with no separate canonical: strip the partner from the name.
  - Fix Ausenco kind (absorbed the Ausenco/Associated JV-string; it is an
    engineering competitor, not an architect).
  Companion to the 21 BdCanonicalDedup merges of 2026-06-19.
*/
BEGIN TRAN;

-- Named JVs -> clean entity name + member Notes
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Affinity Partnerships',
  Notes=COALESCE(Notes,N'JV: Ledcor + Balfour Beatty.'), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=71494;
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Bow Transit Connectors',
  Notes=COALESCE(Notes,N'JV: Flatiron + Barnard.'), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=75858;
UPDATE opportunities.CanonicalOrg SET DisplayName=N'South Fraser Station Partners',
  Notes=COALESCE(Notes,N'JV: Aecon + Acciona + Pomerleau.'), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=76239;
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Concert-Bird Partners',
  Notes=COALESCE(Notes,N'JV: Bird Design-Build + Wright Construction Western (Alberta P3 schools consortium).'), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=76635;

-- Split-leads: strip the partner from the name (no separate canonical existed)
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Thomas Leung Structural Engineering Inc.',
  Notes=COALESCE(Notes,N'Was teamed with Opal Engineering on the linked project.'), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=69958;
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Braniff Construction Ltd.',
  Notes=COALESCE(Notes,N'Was teamed with Faction Construction on the linked project.'), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=70673;
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Beacon',
  Notes=COALESCE(Notes,N'Beacon Clinic (Calgary); partner undisclosed/private.'), UpdatedAtUtc=sysdatetimeoffset() WHERE Id=68823;

-- Ausenco: absorbed the Ausenco/Associated JV-string; fix kind + canonical name
UPDATE opportunities.CanonicalOrg SET DisplayName=N'Ausenco', Kind=N'Competitor', UpdatedAtUtc=sysdatetimeoffset() WHERE Id=64926;

PRINT 'Migration 224: JV-string org names cleaned.';
COMMIT TRAN;
GO
