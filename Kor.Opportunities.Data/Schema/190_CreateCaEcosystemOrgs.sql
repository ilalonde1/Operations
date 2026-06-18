/*
190_CreateCaEcosystemOrgs.sql
WHAT: Create the genuinely-absent BD-target orgs surfaced by the 2026-06-18 CA ecosystem
      wave that have decision-maker contacts attached (verified absent via targeted lookup).
WHY:  Org foundation for ingesting the verified-email decision-makers (migration 191).
HOW:  Dup-safe IF NOT EXISTS on computed NormalizedName (per 120/183/187 idiom). Reuses for
      existing firms (Irvine Co 74271, KTGY 76890, Hines 68778, Tucker Sadler 75177, Safdie
      Rabines 68910, SCB 75180, Trammell Crow 75736, Mitsui 74239, AvalonBay 64, Sares Regis
      75186, Hankey/Jamison 74231) are handled in 191's map — not recreated here.
*/
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @now datetimeoffset = sysdatetimeoffset();
DECLARE @o TABLE (DisplayName nvarchar(300), Kind nvarchar(40), Notes nvarchar(max), Norm nvarchar(300));
INSERT INTO @o (DisplayName, Kind, Notes) VALUES
 (N'Studio E Architects', N'Architect', N'San Diego architect-Prime (Eric Naslund, John Sheehan). Midway Rising context. [ca-ecosystem-2026-06]'),
 (N'Zephyr Partners', N'Developer', N'San Diego developer (Brad Termini, Ryan Herrell). [ca-ecosystem-2026-06]'),
 (N'Chelsea Investment Corporation', N'Developer', N'San Diego affordable-housing developer (CDLAC/TCAC lane). Schmid/Andersen/Hoffman. [ca-ecosystem-2026-06]'),
 (N'Wakeland Housing', N'Developer', N'San Diego affordable developer (Rebecca Louie); Riverwalk affordable component. [ca-ecosystem-2026-06]'),
 (N'Affirmed Housing', N'Developer', N'San Diego affordable-housing developer (woodframe/TCAC). [ca-ecosystem-2026-06]'),
 (N'Architects Orange', N'Architect', N'OC architect-Prime ("AO"; Rob Budetti). Bolsa Pacific, Hive Live, Greystar Row at Red Hill. [ca-ecosystem-2026-06]'),
 (N'DFH Architects', N'Architect', N'Los Angeles architect-Prime. [ca-ecosystem-2026-06]'),
 (N'H&S Ventures', N'Developer', N'Samueli family development vehicle (OC Vibe, Anaheim). [ca-ecosystem-2026-06]'),
 (N'Nadel Architecture', N'Architect', N'LA architect-Prime (Rise Hollywood / Rise Koreatown). [ca-ecosystem-2026-06]'),
 (N'Picerne Group', N'Developer', N'OC developer (Avalon Aliso Viejo w/ TCA). [ca-ecosystem-2026-06]'),
 (N'Alliance Residential', N'Developer', N'National multifamily developer, active CA. [ca-ecosystem-2026-06]'),
 (N'Legacy Partners', N'Developer', N'Multifamily developer (OC + Bay Area). [ca-ecosystem-2026-06]'),
 (N'Affinius Capital', N'Developer', N'Capital partner/developer, OC multifamily. [ca-ecosystem-2026-06]'),
 (N'Lyon Living', N'Developer', N'OC multifamily developer (Lyon Living / Eagle Four Partners). [ca-ecosystem-2026-06]');
UPDATE @o SET Norm = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(DisplayName))),N' ',N''),N'.',N''),N',',N''),N'''',N''),N'-',N''),N'&',N''),N'/',N''),N'(',N''),N')',N''),N'+',N'');
MERGE opportunities.CanonicalOrg WITH (HOLDLOCK) AS T
USING (SELECT DisplayName, Kind, Notes, Norm FROM @o) AS S
  ON T.NormalizedName = S.Norm AND T.RetiredAtUtc IS NULL
WHEN NOT MATCHED THEN INSERT (Kind, DisplayName, Notes, KorProjectsCount, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.Kind, S.DisplayName, S.Notes, 0, @now, @now);
COMMIT;
SELECT Id, Kind, DisplayName FROM opportunities.CanonicalOrg
WHERE DisplayName IN (SELECT DisplayName FROM @o) ORDER BY Kind, DisplayName;
