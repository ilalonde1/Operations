/*
  287_CrmEngagementContact.sql  (2026-07-17)

  CRM Neural Gap Register G1: relationship-first engagements (Terry Gray,
  Elliot Wood — both opened the week of 2026-07-13) anchor to an org but
  cannot reference the human being met. Add a nullable contact FK into the
  intel graph so an engagement can say WHO, not just WHERE.

  Backfill: engagement 375 (Ledcor Kelowna, opened 2026-07-17) -> IntelPerson
  20300 (Elliot Wood, Apollo+Hunter verified) — the row that exposed the gap.

  App/Worker readers LEFT JOIN this; NULL stays valid for opportunity-anchored
  engagements (the Deltek.CustomProposal backfill population).
*/

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'opportunities.CrmEngagements')
      AND name = N'ContactIntelPersonId')
BEGIN
    ALTER TABLE opportunities.CrmEngagements
        ADD ContactIntelPersonId BIGINT NULL
            CONSTRAINT FK_CrmEngagements_ContactIntelPerson
            REFERENCES opportunities.IntelPerson (Id);
END
GO

-- Backfill the engagement that exposed the gap (guarded: only if both rows
-- exist and the contact is still unset).
UPDATE e SET e.ContactIntelPersonId = 20300, e.UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CrmEngagements e
WHERE e.Id = 375
  AND e.ContactIntelPersonId IS NULL
  AND EXISTS (SELECT 1 FROM opportunities.IntelPerson p WHERE p.Id = 20300 AND p.RetiredAtUtc IS NULL);
GO
