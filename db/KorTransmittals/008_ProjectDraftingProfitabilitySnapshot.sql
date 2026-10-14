/*
008_ProjectDraftingProfitabilitySnapshot.sql  (2026-10-14)

Banks the CAD-vs-Revit profitability measurement as rows the app and /ask can read,
instead of a spreadsheet somebody keeps on a desktop.

Why: two managers were tallying "how much CAD do we do" by opening job folders and
counting files. The answer is in Deltek, weighted by fee, billings and drafting hours,
and the Financials module already computes every ingredient. This table is the per-job
snapshot of those ingredients on a date; the view adds the derived figures; the proc
refills it from Deltek through the DELTEK_VP linked server with no tooling outside SQL.

Method (matches Kor.Operations.PMTools.ProjectAnalyticsService + HistoricalAnalyticsViewModel,
reproduced to the dollar on six projects before this was written):
  - top-level regular projects only: PR.ChargeType='R', WBS2 and WBS3 blank
  - hours from tkDetail, rejected lines excluded; Engineering = LaborCode 10 + 30,
    Drafting = 20; BillableHrs = everything but Admin (70) / NonBillable (80) on real jobs
  - billed = PRSummaryMain, BilledFee when non-zero else Revenue
  - cost = hours x the employee's CURRENT cost rate (EMCompany.ProvCostRate);
    partners (Employee starting with 'P') imputed at $250/hr, the app's PartnerImputedCostRate default
  - LaborCostActiveStaff = the app's Historical screen figure (active employees only);
    LaborCostAllStaff adds terminated staff, who logged 40% of the firm's billable hours
    and are priced at zero by the app. The report uses AllStaff; both are kept.
  - fee and billed on Org='USA' jobs converted at @UsdToCadRate (app default 1.36); cost stays CAD
  - Sector = PR.ProjectType (Res, Com, Hotel, Off, Ind-Garage, Inst-Rec-C, R-S-R, Small Job)
  - DraftingType = ProjectCustomTabFields.CustDraftingType (RV, CAD, General, '' = untagged)

Run in KorTransmittals as sa (transmittals_app has datareader/datawriter only, no DDL).
Re-runnable; a re-run for the same AsOfDate replaces that date.
  EXEC dbo.usp_SnapshotProjectDraftingProfitability;                      -- today
  EXEC dbo.usp_SnapshotProjectDraftingProfitability @AsOfDate = '2026-10-14';
*/

USE KorTransmittals;
IF DB_NAME() <> N'KorTransmittals' BEGIN RAISERROR('Wrong database on server %s. This runs ONLY in KorTransmittals.', 20, 1, @@SERVERNAME) WITH LOG; END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.ProjectDraftingProfitabilitySnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectDraftingProfitabilitySnapshot
    (
        Id                     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectDraftingProfitabilitySnapshot PRIMARY KEY,
        AsOfDate               DATE           NOT NULL,
        Wbs1                   NVARCHAR(30)   NOT NULL,
        ProjectName            NVARCHAR(100)  NOT NULL CONSTRAINT DF_PDPS_ProjectName DEFAULT (''),
        Status                 NCHAR(1)       NULL,
        Org                    NVARCHAR(20)   NOT NULL CONSTRAINT DF_PDPS_Org DEFAULT (''),
        Sector                 NVARCHAR(20)   NOT NULL CONSTRAINT DF_PDPS_Sector DEFAULT (''),
        DraftingType           NVARCHAR(20)   NOT NULL CONSTRAINT DF_PDPS_DraftingType DEFAULT (''),
        Category               NVARCHAR(40)   NOT NULL CONSTRAINT DF_PDPS_Category DEFAULT (''),
        ConstructionType       NVARCHAR(60)   NOT NULL CONSTRAINT DF_PDPS_ConstructionType DEFAULT (''),
        OpenDate               DATE           NULL,
        FirstHoursDate         DATE           NULL,
        LastHoursDate          DATE           NULL,
        UsdToCadRate           DECIMAL(6,4)   NOT NULL,
        FeeCad                 DECIMAL(18,2)  NOT NULL,
        FeeBilledCad           DECIMAL(18,2)  NOT NULL,
        EngHrs                 DECIMAL(12,2)  NOT NULL,
        DraftHrs               DECIMAL(12,2)  NOT NULL,
        BillableHrs            DECIMAL(12,2)  NOT NULL,
        EngCost                DECIMAL(18,2)  NOT NULL,
        DraftCost              DECIMAL(18,2)  NOT NULL,
        LaborCostAllStaff      DECIMAL(18,2)  NOT NULL,
        LaborCostActiveStaff   DECIMAL(18,2)  NOT NULL,
        CreatedUtc             DATETIME2(0)   NOT NULL CONSTRAINT DF_PDPS_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_ProjectDraftingProfitabilitySnapshot UNIQUE (AsOfDate, Wbs1)
    );
    CREATE INDEX IX_PDPS_AsOf_Sector_Type ON dbo.ProjectDraftingProfitabilitySnapshot (AsOfDate, Sector, DraftingType);
END;
GO

IF OBJECT_ID(N'dbo.DraftingHoursByYearSnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DraftingHoursByYearSnapshot
    (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DraftingHoursByYearSnapshot PRIMARY KEY,
        AsOfDate       DATE          NOT NULL,
        [Year]         INT           NOT NULL,
        Sector         NVARCHAR(20)  NOT NULL CONSTRAINT DF_DHYS_Sector DEFAULT (''),
        DraftingType   NVARCHAR(20)  NOT NULL CONSTRAINT DF_DHYS_DraftingType DEFAULT (''),
        DraftHrs       DECIMAL(12,2) NOT NULL,
        Jobs           INT           NOT NULL,
        CreatedUtc     DATETIME2(0)  NOT NULL CONSTRAINT DF_DHYS_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_DraftingHoursByYearSnapshot UNIQUE (AsOfDate, [Year], Sector, DraftingType)
    );
END;
GO

/* Derived figures, one place. Completed = billed at least 90% of a non-zero fee: the only
   rows on which margin means anything. In-flight rows carry cost ahead of billing. */
CREATE OR ALTER VIEW dbo.vw_ProjectDraftingProfitability AS
SELECT
    s.*,
    CASE WHEN s.DraftingType = N'' THEN N'untagged' ELSE s.DraftingType END                       AS DraftingTypeLabel,
    CASE WHEN s.FeeCad > 0 AND s.FeeBilledCad >= 0.9 * s.FeeCad THEN 1 ELSE 0 END                 AS IsCompleted,
    s.EngHrs + s.DraftHrs                                                                         AS ProductionHrs,
    s.FeeBilledCad - s.LaborCostAllStaff                                                          AS MarginAllStaff,
    s.FeeBilledCad - s.LaborCostActiveStaff                                                       AS MarginActiveStaff,
    CASE WHEN s.FeeBilledCad <> 0 THEN (s.FeeBilledCad - s.LaborCostAllStaff) / s.FeeBilledCad END AS MarginPctAllStaff,
    CASE WHEN s.EngHrs + s.DraftHrs > 0 THEN s.FeeBilledCad / (s.EngHrs + s.DraftHrs) END         AS BilledPerProductionHr,
    CASE WHEN s.DraftHrs > 0 THEN s.FeeBilledCad / s.DraftHrs END                                 AS BilledPerDraftHr,
    CASE WHEN s.DraftHrs > 0 THEN s.DraftCost / s.DraftHrs END                                    AS DraftCostPerHr,
    CASE WHEN s.FeeBilledCad <> 0 THEN s.DraftCost / s.FeeBilledCad END                           AS DraftCostShareOfBilled,
    CASE WHEN s.EngHrs + s.DraftHrs > 0 THEN s.DraftHrs / (s.EngHrs + s.DraftHrs) END             AS DraftShareOfHrs,
    YEAR(s.FirstHoursDate)                                                                        AS FirstHoursYear
FROM dbo.ProjectDraftingProfitabilitySnapshot s;
GO

CREATE OR ALTER PROCEDURE dbo.usp_SnapshotProjectDraftingProfitability
    @AsOfDate     DATE         = NULL,
    @UsdToCadRate DECIMAL(6,4) = 1.36
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @AsOfDate = COALESCE(@AsOfDate, CAST(SYSDATETIME() AS DATE));

    /* Pulled into a temp table first so a Deltek failure leaves the previous snapshot intact. */
    SELECT *
    INTO #jobs
    FROM OPENQUERY([DELTEK_VP], '
        SELECT
            pr.WBS1,
            LEFT(pr.Name, 100)                                         AS Name,
            pr.Status,
            LTRIM(RTRIM(COALESCE(pr.Org, '''')))                        AS Org,
            pr.OpenDate,
            LTRIM(RTRIM(COALESCE(pr.ProjectType, '''')))                AS Sector,
            LTRIM(RTRIM(COALESCE(pctf.CustDraftingType, '''')))         AS DraftingType,
            LTRIM(RTRIM(COALESCE(pctf.CustProjectCategory, '''')))      AS Category,
            LTRIM(RTRIM(COALESCE(pctf.CustConstructionType, '''')))     AS ConstructionType,
            CAST(COALESCE(pr.Fee, 0) AS DECIMAL(18,2))                  AS Fee,
            CAST(COALESCE(billed.FeeBilled, 0) AS DECIMAL(18,2))        AS FeeBilled,
            CAST(L.EngHrs AS DECIMAL(12,2))                             AS EngHrs,
            CAST(L.DraftHrs AS DECIMAL(12,2))                           AS DraftHrs,
            CAST(L.BillableHrs AS DECIMAL(12,2))                        AS BillableHrs,
            CAST(L.EngCost AS DECIMAL(18,2))                            AS EngCost,
            CAST(L.DraftCost AS DECIMAL(18,2))                          AS DraftCost,
            CAST(L.CostAll AS DECIMAL(18,2))                            AS CostAll,
            CAST(L.CostActive AS DECIMAL(18,2))                         AS CostActive,
            L.FirstHrs,
            L.LastHrs
        FROM [C0000052267P_1_KOR00000000].dbo.PR pr
        LEFT JOIN [C0000052267P_1_KOR00000000].dbo.ProjectCustomTabFields pctf
               ON pctf.WBS1 = pr.WBS1 AND pctf.WBS2 = pr.WBS2 AND pctf.WBS3 = pr.WBS3
        LEFT JOIN (
            SELECT WBS1, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) AS FeeBilled
            FROM [C0000052267P_1_KOR00000000].dbo.PRSummaryMain
            GROUP BY WBS1
        ) billed ON billed.WBS1 = pr.WBS1
        LEFT JOIN (
            SELECT t.WBS1,
                SUM(CASE WHEN t.LaborCode IN (10,30) THEN (COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) ELSE 0 END) AS EngHrs,
                SUM(CASE WHEN t.LaborCode = 20      THEN (COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) ELSE 0 END) AS DraftHrs,
                SUM(CASE WHEN t.LaborCode NOT IN (70,80) AND t.WBS1 NOT LIKE ''[A-Z]%'' AND t.WBS1 NOT LIKE ''9[A-Z]%'' AND t.WBS1 NOT LIKE ''99%''
                         THEN (COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) ELSE 0 END) AS BillableHrs,
                SUM(CASE WHEN t.LaborCode IN (10,30) THEN (COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) * (CASE WHEN t.Employee LIKE ''P%'' THEN 250.0 ELSE COALESCE(ec.ProvCostRate,0) END) ELSE 0 END) AS EngCost,
                SUM(CASE WHEN t.LaborCode = 20      THEN (COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) * (CASE WHEN t.Employee LIKE ''P%'' THEN 250.0 ELSE COALESCE(ec.ProvCostRate,0) END) ELSE 0 END) AS DraftCost,
                SUM(CASE WHEN t.LaborCode NOT IN (70,80) AND t.WBS1 NOT LIKE ''[A-Z]%'' AND t.WBS1 NOT LIKE ''9[A-Z]%'' AND t.WBS1 NOT LIKE ''99%''
                         THEN (COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) * (CASE WHEN t.Employee LIKE ''P%'' THEN 250.0 ELSE COALESCE(ec.ProvCostRate,0) END) ELSE 0 END) AS CostAll,
                SUM(CASE WHEN t.LaborCode NOT IN (70,80) AND t.WBS1 NOT LIKE ''[A-Z]%'' AND t.WBS1 NOT LIKE ''9[A-Z]%'' AND t.WBS1 NOT LIKE ''99%''
                          AND UPPER(COALESCE(ec.Status, ''A'')) = ''A''
                         THEN (COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) * (CASE WHEN t.Employee LIKE ''P%'' THEN 250.0 ELSE COALESCE(ec.ProvCostRate,0) END) ELSE 0 END) AS CostActive,
                MIN(t.TransDate) AS FirstHrs,
                MAX(t.TransDate) AS LastHrs
            FROM [C0000052267P_1_KOR00000000].dbo.tkDetail t
            LEFT JOIN [C0000052267P_1_KOR00000000].dbo.EMCompany ec ON ec.Employee = t.Employee
            WHERE t.Employee IS NOT NULL AND COALESCE(t.LineItemApprovalStatus, '''') <> ''R''
            GROUP BY t.WBS1
        ) L ON L.WBS1 = pr.WBS1
        WHERE pr.ChargeType = ''R''
          AND LTRIM(RTRIM(COALESCE(pr.WBS2, ''''))) = ''''
          AND LTRIM(RTRIM(COALESCE(pr.WBS3, ''''))) = ''''
          AND COALESCE(L.EngHrs, 0) + COALESCE(L.DraftHrs, 0) > 0
    ');

    SELECT *
    INTO #years
    FROM OPENQUERY([DELTEK_VP], '
        SELECT YEAR(t.TransDate) AS Yr,
               LTRIM(RTRIM(COALESCE(pr.ProjectType, '''')))         AS Sector,
               LTRIM(RTRIM(COALESCE(pctf.CustDraftingType, ''''))) AS DraftingType,
               CAST(SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS DECIMAL(12,2)) AS DraftHrs,
               COUNT(DISTINCT t.WBS1) AS Jobs
        FROM [C0000052267P_1_KOR00000000].dbo.tkDetail t
        JOIN [C0000052267P_1_KOR00000000].dbo.PR pr
          ON pr.WBS1 = t.WBS1 AND LTRIM(RTRIM(COALESCE(pr.WBS2, ''''))) = '''' AND LTRIM(RTRIM(COALESCE(pr.WBS3, ''''))) = ''''
        LEFT JOIN [C0000052267P_1_KOR00000000].dbo.ProjectCustomTabFields pctf
          ON pctf.WBS1 = pr.WBS1 AND pctf.WBS2 = pr.WBS2 AND pctf.WBS3 = pr.WBS3
        WHERE t.LaborCode = 20 AND COALESCE(t.LineItemApprovalStatus, '''') <> ''R'' AND pr.ChargeType = ''R''
        GROUP BY YEAR(t.TransDate), LTRIM(RTRIM(COALESCE(pr.ProjectType, ''''))), LTRIM(RTRIM(COALESCE(pctf.CustDraftingType, '''')))
    ');

    BEGIN TRANSACTION;

    DELETE FROM dbo.ProjectDraftingProfitabilitySnapshot WHERE AsOfDate = @AsOfDate;
    INSERT INTO dbo.ProjectDraftingProfitabilitySnapshot
        (AsOfDate, Wbs1, ProjectName, Status, Org, Sector, DraftingType, Category, ConstructionType,
         OpenDate, FirstHoursDate, LastHoursDate, UsdToCadRate, FeeCad, FeeBilledCad,
         EngHrs, DraftHrs, BillableHrs, EngCost, DraftCost, LaborCostAllStaff, LaborCostActiveStaff)
    SELECT
        @AsOfDate,
        LTRIM(RTRIM(j.WBS1)),
        COALESCE(j.Name, ''),
        j.Status,
        j.Org, j.Sector, j.DraftingType, j.Category, j.ConstructionType,
        CAST(j.OpenDate AS DATE), CAST(j.FirstHrs AS DATE), CAST(j.LastHrs AS DATE),
        CASE WHEN j.Org = N'USA' THEN @UsdToCadRate ELSE 1.0 END,
        j.Fee       * CASE WHEN j.Org = N'USA' THEN @UsdToCadRate ELSE 1.0 END,
        j.FeeBilled * CASE WHEN j.Org = N'USA' THEN @UsdToCadRate ELSE 1.0 END,
        COALESCE(j.EngHrs, 0), COALESCE(j.DraftHrs, 0), COALESCE(j.BillableHrs, 0),
        COALESCE(j.EngCost, 0), COALESCE(j.DraftCost, 0), COALESCE(j.CostAll, 0), COALESCE(j.CostActive, 0)
    FROM #jobs j;

    DELETE FROM dbo.DraftingHoursByYearSnapshot WHERE AsOfDate = @AsOfDate;
    INSERT INTO dbo.DraftingHoursByYearSnapshot (AsOfDate, [Year], Sector, DraftingType, DraftHrs, Jobs)
    SELECT @AsOfDate, y.Yr, y.Sector, y.DraftingType, COALESCE(y.DraftHrs, 0), y.Jobs
    FROM #years y;

    COMMIT TRANSACTION;

    SELECT @AsOfDate AS AsOfDate,
           (SELECT COUNT(*) FROM dbo.ProjectDraftingProfitabilitySnapshot WHERE AsOfDate = @AsOfDate) AS JobRows,
           (SELECT COUNT(*) FROM dbo.DraftingHoursByYearSnapshot WHERE AsOfDate = @AsOfDate)          AS YearRows;
END;
GO
