USE [KorOpportunitiesDb];
GO

/* =====================================================================
   182 — Normalize California MPI sector taxonomy.
   The CA metro research ingest wrote 70+ free-text Sector values
   ("Affordable multifamily housing", "Multifamily Residential",
   "High-Rise Multifamily", "Mixed-Use/Retail/Entertainment", etc.).
   Collapse to a canonical ~17-value vocabulary so regional/sector
   rollups are clean. Pattern-ordered specific -> general; first match
   wins. CA active rows only.
   ===================================================================== */
SET NOCOUNT ON;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

UPDATE opportunities.MajorProjectsInventory
SET Sector = CASE
  WHEN Sector LIKE '%seismic%' OR Sector LIKE '%retrofit%' THEN 'Seismic Retrofit'
  WHEN Sector LIKE '%life scien%' OR Sector LIKE '%research lab%' OR Sector LIKE '%medical research%' OR Sector LIKE '%research/innovation%' OR Sector LIKE '%academic/lab%' OR Sector LIKE '%research building%' THEN 'Life Sciences'
  WHEN Sector LIKE '%student housing%' THEN 'Student Housing'
  WHEN Sector LIKE '%affordable%' OR Sector LIKE '%supportive%' OR Sector LIKE '%workforce%' THEN 'Affordable Housing'
  WHEN Sector LIKE '%health%' OR Sector LIKE '%hospital%' OR Sector LIKE '%medical%' THEN 'Healthcare'
  WHEN Sector LIKE '%higher education%' OR Sector LIKE '%university%' OR Sector LIKE '%college%' OR Sector LIKE '%campus%' OR Sector LIKE '%academic%' THEN 'Higher Education'
  WHEN Sector LIKE '%k-12%' OR Sector LIKE '%educational%' OR Sector LIKE '%education%' OR Sector LIKE '%school%' THEN 'Education'
  WHEN Sector LIKE '%hospitality%' OR Sector LIKE '%hotel%' THEN 'Hospitality'
  WHEN Sector LIKE '%stadium%' OR Sector LIKE '%sports%' OR Sector LIKE '%arena%' OR Sector LIKE '%athletic%' OR Sector LIKE '%recreation%' THEN 'Sports/Recreation'
  WHEN Sector LIKE '%entertainment%' OR Sector LIKE '%studio%' THEN 'Entertainment/Studio'
  WHEN Sector LIKE '%mixed-use%' OR Sector LIKE '%mixed use%' THEN 'Mixed-Use'
  WHEN Sector LIKE '%high-rise%' OR Sector LIKE '%high rise%' OR Sector LIKE '%luxury high%' THEN 'Residential High-Rise'
  WHEN Sector LIKE '%multifamily%' OR Sector LIKE '%multi-family%' OR Sector LIKE '%residential%' OR Sector LIKE '%townhome%' OR Sector LIKE '%apartment%' OR Sector LIKE '%housing%' THEN 'Multifamily'
  WHEN Sector LIKE '%civic%' OR Sector LIKE '%government%' OR Sector LIKE '%state office%' OR Sector LIKE '%performing arts%' OR Sector LIKE '%library%' OR Sector LIKE '%museum%' OR Sector LIKE '%convention%' THEN 'Civic'
  WHEN Sector LIKE '%office%' OR Sector LIKE '%commercial%' THEN 'Commercial'
  WHEN Sector LIKE '%transportation%' OR Sector LIKE '%transit%' THEN 'Transportation'
  WHEN Sector LIKE '%industrial%' OR Sector LIKE '%utilities%' THEN 'Industrial'
  ELSE Sector END,
    UpdatedAtUtc = @now
WHERE RetiredAtUtc IS NULL AND Province = 'CA' AND Sector IS NOT NULL;
GO
