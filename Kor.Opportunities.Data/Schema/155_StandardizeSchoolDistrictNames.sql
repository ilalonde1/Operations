USE [KorOpportunitiesDb];
GO

/* =====================================================================
   155 — Standardize BC school-district survivor names to one format.
   ---------------------------------------------------------------------
   After the systematic SD dedup (sd-dedup-2026-06-16), the surviving district
   rows were in mixed formats ("SD61 Greater Victoria School District" vs
   "Richmond School District (SD38)" vs bare "School District 23"). The majority
   already use "SD<NN> <Place> School District", so the minority are aligned to
   that form for a consistent owner column in every regional report. Vancouver
   keeps its recognized "Vancouver School Board (SD39)" brand. Idempotent.
   ===================================================================== */

UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD5 Southeast Kootenay School District', UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=70002;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD8 Kootenay Lake School District',       UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=70045;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD22 Vernon School District',             UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=75964;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD23 Central Okanagan School District',   UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=54415;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD28 Quesnel School District',            UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=75751;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD33 Chilliwack School District',         UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=75752;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD34 Abbotsford School District',         UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=68848;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD35 Langley School District',            UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=54963;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD36 Surrey School District',             UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=54964;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD37 Delta School District',              UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=54965;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD38 Richmond School District',           UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=68851;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD40 New Westminster School District',    UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=53687;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD42 Maple Ridge-Pitt Meadows School District', UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=69301;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD44 North Vancouver School District',    UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=75852;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD45 West Vancouver School District',     UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=69571;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD53 Okanagan Similkameen School District', UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=69569;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD73 Kamloops-Thompson School District',  UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=18900;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD83 North Okanagan-Shuswap School District', UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=69177;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'SD93 Conseil Scolaire Francophone',       UpdatedAtUtc=SYSDATETIMEOFFSET() WHERE Id=53773;
GO
