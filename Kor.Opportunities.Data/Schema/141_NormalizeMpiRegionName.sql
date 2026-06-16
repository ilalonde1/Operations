USE [KorOpportunitiesDb];
GO

/* =====================================================================
   141 — Normalize MajorProjectsInventory.RegionName to a canonical set.
   ---------------------------------------------------------------------
   The region label was free-text from many importers and had drifted into
   ~40 variants for the same four BD regions ("LowerMainland" / "Lower Mainland"
   / "Metro Vancouver" / "2. Mainland/Southwest"; "Okanagan" / "Okanagan/Interior"
   / "3. Thompson-Okanagan"; etc.), plus a real source bug where lat/long
   COORDINATES leaked into RegionName on ~40 Alberta rows ("(51.04, -114.07)").
   That made regional rollups / dashboards / the enrichment gap-map unsliceable.

   This collapses BC + AB region labels to a canonical taxonomy:
     BC: 'Lower Mainland' | 'Vancouver Island' | 'Okanagan/Interior' | 'Northern BC'
     AB: 'Calgary Metro' | 'Edmonton Metro' | 'Southern Alberta' | 'Central Alberta'
   Unrecognized / junk / coordinate-leak values -> NULL (unspecified), which the
   readers already treat as "(none)". US/other provinces are left untouched.

   Idempotent: re-running maps the canonical values to themselves.
   NOTE (source fix, follow-up): trace which importer wrote coordinates into
   RegionName (likely the AB MPI provider mapping the wrong column) and add a
   shared RegionNormalizer at the write path so this can't re-drift.
   ===================================================================== */

UPDATE opportunities.MajorProjectsInventory
SET RegionName =
    CASE
        WHEN Province = N'BC' AND (
                 RegionName LIKE N'%Island%' OR RegionName LIKE N'%Victoria%'
              OR RegionName LIKE N'%Nanaimo%' OR RegionName LIKE N'%Cowichan%'
              OR RegionName LIKE N'%Comox%'   OR RegionName LIKE N'%Alberni%')
            THEN N'Vancouver Island'
        WHEN Province = N'BC' AND (
                 RegionName LIKE N'%Okanagan%' OR RegionName LIKE N'%Interior%'
              OR RegionName LIKE N'%Thompson%' OR RegionName LIKE N'%Kootenay%')
            THEN N'Okanagan/Interior'
        WHEN Province = N'BC' AND (
                 RegionName LIKE N'%Mainland%' OR RegionName LIKE N'%Metro Vancouver%'
              OR RegionName = N'Vancouver'     OR RegionName = N'Burnaby'
              OR RegionName LIKE N'%Fraser%'   OR RegionName LIKE N'%Sea-to-Sky%'
              OR RegionName LIKE N'%SeaToSky%')
            THEN N'Lower Mainland'
        WHEN Province = N'BC' AND RegionName LIKE N'%Northern%'
            THEN N'Northern BC'
        WHEN Province = N'BC'
            THEN NULL  -- 'BC' / 'Broader BC' / 'Other' / 'Fraser Health' / null / junk

        WHEN Province = N'AB' AND RegionName LIKE N'%Calgary%'  THEN N'Calgary Metro'
        WHEN Province = N'AB' AND RegionName LIKE N'%Edmonton%' THEN N'Edmonton Metro'
        WHEN Province = N'AB' AND RegionName LIKE N'%Southern%' THEN N'Southern Alberta'
        WHEN Province = N'AB' AND RegionName LIKE N'%Central%'  THEN N'Central Alberta'
        WHEN Province = N'AB'
            THEN NULL  -- 'Alberta' / 'Other AB' / coordinate-leak '(lat, long)' / junk

        ELSE RegionName   -- US / other provinces untouched
    END
WHERE Province IN (N'BC', N'AB');
GO
