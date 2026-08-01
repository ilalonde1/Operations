/*
183_CreateCaSynthesisOrgs.sql
WHAT: Create the CanonicalOrg rows verified in the 2026-06 California BD synthesis
      that were absent from the org graph (audited 2026-06-18, none exist).
WHY:  Stage 1 of decomposing the CA synthesis intelligence into the DB. These orgs are
      the SE incumbents / architects / developers the verified seats link to (Stage 2).
HOW:  Dup-safe IF NOT EXISTS on the (computed, non-insertable) NormalizedName literal —
      the proven pattern from 120_LoblawParentRemint.sql. Lookalikes confirmed distinct:
      "KFA Homes Ltd"(29280)=BC homebuilder, "CityView"(4906)=permitting software,
      "Pacifica Housing"(53723)=BC nonprofit — NOT these orgs.
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'nelsonstructuralengineers' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Competitor', N'Nelson Structural Engineers', N'Irvine, CA SE. House SE for the Holland Partner / MVE NorCal multifamily pipeline (Gateway Crossing, Santa Clara). ~11 staff, no Bay Area office; brought FBA Inc. (Hayward) as co-SE = capacity-stretched. KOR''s #1 NorCal displacement target. [ca-research-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'pbaengineering' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Competitor', N'PBA Engineering', N'Irvine, CA SE. Dominant Orange County multifamily SE (locks KTGY/TCA/AO/JPI). Volume-practice; complexity ceiling on Type I PT-concrete towers + 8+ story = KOR''s OC wedge. [ca-research-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'mvepartners' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Architect', N'MVE + Partners', N'Irvine HQ + LA/SD/SF. Major CA multifamily/mixed-use architect-Prime (~100 staff). Matthew McLarand (President) = Jim DesRoches'' personal contact. SE defaults: Englekirk/WSP (SoCal MF), Nelson (NorCal), Glotman (Ritz-Carlton Newport). [ca-research-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'pacificacompanies' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Developer', N'Pacifica Companies', N'San Diego developer (Israni family). Amara Bay / Chula Vista Bayfront (~1,500u + hotel). Selects the SE directly (not AVRP). Distinct from "Pacifica Housing"(53723)=BC nonprofit. [ca-research-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'linklogisticsrealestate' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Developer', N'Link Logistics Real Estate', N'Developer of 5757 Uplander Way / Fox Hills (Culver City, 1,077u; KFA architect) — KOR''s one genuinely-open LA architect seat (red-team-verified, 6-12mo window). [ca-research-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'killeferflammangarchitects' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Architect', N'Killefer Flammang Architects', N'KFA, Los Angeles. Architect on 5757 Uplander/Fox Hills (Link Logistics, 1,077u). Distinct from "KFA Homes Ltd"(29280)=BC homebuilder. [ca-research-2026-06]', @now, @now);

IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg WHERE NormalizedName = N'tcaarchitects' AND RetiredAtUtc IS NULL)
  INSERT INTO opportunities.CanonicalOrg (Kind, DisplayName, Notes, CreatedAtUtc, UpdatedAtUtc)
  VALUES (N'Architect', N'TCA Architects', N'Architect on Holland''s 3896 Stevens Creek (San Jose, 532u) — KOR''s #1 NorCal seat; a second entry channel outside the Nelson/MVE trust chain. [ca-research-2026-06]', @now, @now);

COMMIT;

-- Verify (match on DisplayName so the computed NormalizedName + new Id always surface):
SELECT Id, Kind, DisplayName, NormalizedName
FROM opportunities.CanonicalOrg
WHERE DisplayName IN (N'Nelson Structural Engineers', N'PBA Engineering', N'MVE + Partners',
                      N'Pacifica Companies', N'Link Logistics Real Estate',
                      N'Killefer Flammang Architects', N'TCA Architects')
ORDER BY DisplayName;
