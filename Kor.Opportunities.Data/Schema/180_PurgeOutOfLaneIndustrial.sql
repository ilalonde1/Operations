USE [KorOpportunitiesDb];
GO

/* =====================================================================
   180 — Retire out-of-lane resource-extraction / energy / heavy-industrial
   projects that the StructuralRelevanceGate let through (it had no
   resource/energy deny-tier, and generic keep-words like "expansion",
   "facility", "terminal" waved mines/LNG/refineries past). These are not
   building-SE seats and were polluting the new-opportunities feed,
   enrichment queues, and coverage metrics.

   SAFE matching: word-ish patterns only — NO bare 'NGL' (it matches the
   SUBSTRING in La[ngl]ey / I[ngl]ewood / A[ngl]emont), and ' Mine' is
   space-anchored. Excludes the Burrard Inlet Marine Container EXAMINATION
   FACILITY (a real CBSA building, in-lane). Verified list = 46 rows.
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

UPDATE opportunities.MajorProjectsInventory
SET RetiredAtUtc = @now,
    RetiredReason = N'Out-of-lane (resource/energy/heavy-industrial — not a building SE seat) — migration 180',
    UpdatedAtUtc = @now
WHERE RetiredAtUtc IS NULL
  AND ( ProjectName LIKE N'%LNG%' OR ProjectName LIKE N'%Refinery%' OR ProjectName LIKE N'% Mine%' OR ProjectName LIKE N'%Mining%'
     OR ProjectName LIKE N'%Nickel%' OR ProjectName LIKE N'%Copper%' OR ProjectName LIKE N'%Gold Project%' OR ProjectName LIKE N'%Gold Mine%'
     OR ProjectName LIKE N'%Gold-Copper%' OR ProjectName LIKE N'%Porphyry%' OR ProjectName LIKE N'%Gas Transmission%' OR ProjectName LIKE N'%Gas Plant%'
     OR ProjectName LIKE N'%NGL Plant%' OR ProjectName LIKE N'%Petrochem%' OR ProjectName LIKE N'%Wastewater%' OR ProjectName LIKE N'%Sewage%'
     OR ProjectName LIKE N'%Water Treatment Plant%' OR ProjectName LIKE N'%Container%' OR ProjectName LIKE N'%Smelter%' OR ProjectName LIKE N'%Tailings%'
     OR ProjectName LIKE N'%Coal %' OR ProjectName LIKE N'%Hydroelectric%' OR ProjectName LIKE N'%Transmission Line%' )
  AND ProjectName NOT LIKE N'%Examination Facility%';   -- keep the CBSA building
GO
