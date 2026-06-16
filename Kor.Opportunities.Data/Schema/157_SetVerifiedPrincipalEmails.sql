USE [KorOpportunitiesDb];
GO

/* =====================================================================
   157 — Set Hunter-verified emails for the two corrected principals.
   ---------------------------------------------------------------------
   After 154 nulled their wrong emails, Hunter email-finder returned verified
   addresses (status=valid) for both:
     Paul Fast (6165) -> pfast@fastepp.com    (score 98)
     Tom Clark (6931) -> tomclark@ckarch.com  (score 97)
   ===================================================================== */

UPDATE opportunities.IntelPerson
SET Email = N'pfast@fastepp.com', EmailSource = N'Hunter', EmailConfidence = 98,
    EmailCheckedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 6165 AND Email IS NULL;

UPDATE opportunities.IntelPerson
SET Email = N'tomclark@ckarch.com', EmailSource = N'Hunter', EmailConfidence = 97,
    EmailCheckedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 6931 AND Email IS NULL;
GO
