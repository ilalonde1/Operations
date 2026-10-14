/*
008_ProjectDraftingProfitabilitySnapshot.sql  (2026-10-14, revised same day for posted cost)

Banks the CAD-vs-Revit profitability measurement as rows the app and /ask can read,
instead of a spreadsheet somebody keeps on a desktop. The shipped report
docs/KOR-Financials-CAD-vs-Revit-2026-10-14-web.pdf is this measurement; a re-run of the
proc on a later date gives the next edition without any tooling outside SQL.

Why: two managers were tallying "how much CAD do we do" by opening job folders and
counting files. The answer is in Deltek, weighted by fee, billings and drafting hours.

What a row is: one top-level regular project with production hours, as of a date, with
every ingredient of margin on two cost bases, plus a by-year table of hours, posted cost
and billings per sector and drafting type. The view derives the figures the report prints.

Method (matches Kor.Operations.PMTools.ProjectAnalyticsService + HistoricalAnalyticsViewModel
for the current-rate basis, reproduced to the dollar on six projects; FirmHealthService for the
posted basis):
  - top-level regular projects only: PR.ChargeType='R', WBS2 and WBS3 blank
  - hours from tkDetail, rejected lines excluded; Engineering = LaborCode 10 + 30, Drafting = 20;
    Billable = everything but Admin (70) / NonBillable (80) on real job numbers
  - billed = PRSummaryMain, BilledFee when non-zero else Revenue
  - POSTED basis (the report's): tkDetail RegAmt + OvtAmt + SpecialOvtAmt, the payroll amount at the
    rate in effect when the hours were posted. Partner hours (Employee starting with 'P') carry a
    nominal payroll figure, so the view replaces it with PartnerHrs x PartnerImputedRate, the rate
    the proc was run with (default 250, the app's PartnerImputedCostRate). Both figures are stored.
  - CURRENT-RATE basis (the Historical screen's): hours x EMCompany.ProvCostRate today, partners at
    the same imputed rate. ActiveStaff = active employees only, which is what the screen shows;
    AllStaff adds terminated staff, who logged 40% of the firm's billable hours.
  - fee, billed and posted amounts on Org='USA' jobs converted at @UsdToCadRate (app default 1.36);
    current-rate cost is CAD already
  - Sector = PR.ProjectType (Res, Com, Hotel, Off, Ind-Garage, Inst-Rec-C, R-S-R, Small Job)
  - DraftingType = ProjectCustomTabFields.CustDraftingType (RV, CAD, General, '' = untagged)

Run in KorTransmittals as sa (transmittals_app has datareader/datawriter only, no DDL).
Re-runnable; a re-run for the same AsOfDate replaces that date.
  EXEC dbo.usp_SnapshotProjectDraftingProfitability;                                  -- today, 1.36, 250
  EXEC dbo.usp_SnapshotProjectDraftingProfitability @AsOfDate = '2026-10-14';
  EXEC dbo.usp_SnapshotProjectDraftingProfitability @PartnerImputedCostRate = 0;      -- partner hours at no cost at all
  -- partners at their posted payroll amount is a column, not a run: vw_ProjectDraftingProfitability.MarginPartnersAtPayroll
Expect on 2026-10-14: 2,328 job rows (1,316 residential), billed posted through 202610.
Re-running on a database that already has the tables adds any new columns in place (the ALTER blocks)
and takes a fresh snapshot for the date.

The view's IsFinished and IsInWindow are the report's margin population:
  finished  = billed at least 90% of a non-zero fee, or Status not Active (the Historical screen's "Closed")
  in window = no billings posted before 2019 and no OpenDate before 2019. Deltek's timesheets begin in 2019,
              so a job billed before then carries lifetime billings against only part of its labour; its
              margin is not real and the report excludes it. The year table keeps the same window.
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
        Id                       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectDraftingProfitabilitySnapshot PRIMARY KEY,
        AsOfDate                 DATE           NOT NULL,
        Wbs1                     NVARCHAR(30)   NOT NULL,
        ProjectName              NVARCHAR(100)  NOT NULL CONSTRAINT DF_PDPS_ProjectName DEFAULT (''),
        Status                   NCHAR(1)       NULL,
        Org                      NVARCHAR(20)   NOT NULL CONSTRAINT DF_PDPS_Org DEFAULT (''),
        Sector                   NVARCHAR(20)   NOT NULL CONSTRAINT DF_PDPS_Sector DEFAULT (''),
        DraftingType             NVARCHAR(20)   NOT NULL CONSTRAINT DF_PDPS_DraftingType DEFAULT (''),
        Category                 NVARCHAR(40)   NOT NULL CONSTRAINT DF_PDPS_Category DEFAULT (''),
        ConstructionType         NVARCHAR(60)   NOT NULL CONSTRAINT DF_PDPS_ConstructionType DEFAULT (''),
        OpenDate                 DATE           NULL,
        FirstHoursDate           DATE           NULL,
        LastHoursDate            DATE           NULL,
        UsdToCadRate             DECIMAL(6,4)   NOT NULL,
        PartnerImputedRate       DECIMAL(9,2)   NOT NULL,
        FeeCad                   DECIMAL(18,2)  NOT NULL,
        FeeBilledCad             DECIMAL(18,2)  NOT NULL,
        BilledBefore2019Cad      DECIMAL(18,2)  NOT NULL CONSTRAINT DF_PDPS_BilledBefore2019Cad DEFAULT (0),
        FirstBillingPeriod       INT            NULL,
        EngHrs                   DECIMAL(12,2)  NOT NULL,
        DraftHrs                 DECIMAL(12,2)  NOT NULL,
        BillableHrs              DECIMAL(12,2)  NOT NULL,
        PartnerHrs               DECIMAL(12,2)  NOT NULL,   -- billable hours by partners
        PartnerDraftHrs          DECIMAL(12,2)  NOT NULL,   -- of which drafting
        PartnerEngHrs            DECIMAL(12,2)  NOT NULL CONSTRAINT DF_PDPS_PartnerEngHrs DEFAULT (0),   -- of which engineering
        -- posted basis: Deltek's payroll amounts at the rate of the time, CAD
        EngAmtPosted             DECIMAL(18,2)  NOT NULL,
        DraftAmtPosted           DECIMAL(18,2)  NOT NULL,
        LaborAmtPosted           DECIMAL(18,2)  NOT NULL,   -- billable hours, all staff, as posted
        PartnerAmtPosted         DECIMAL(18,2)  NOT NULL,   -- the partners' share of LaborAmtPosted
        PartnerDraftAmtPosted    DECIMAL(18,2)  NOT NULL,   -- the partners' share of DraftAmtPosted
        PartnerEngAmtPosted      DECIMAL(18,2)  NOT NULL CONSTRAINT DF_PDPS_PartnerEngAmtPosted DEFAULT (0),   -- the partners' share of EngAmtPosted
        -- current-rate basis: hours x today's cost rate, partners at PartnerImputedRate, CAD
        EngCostCurrent           DECIMAL(18,2)  NOT NULL,
        DraftCostCurrent         DECIMAL(18,2)  NOT NULL,
        LaborCostCurrentAllStaff DECIMAL(18,2)  NOT NULL,
        LaborCostCurrentActive   DECIMAL(18,2)  NOT NULL,   -- the Historical screen's figure
        CreatedUtc               DATETIME2(0)   NOT NULL CONSTRAINT DF_PDPS_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_ProjectDraftingProfitabilitySnapshot UNIQUE (AsOfDate, Wbs1)
    );
    CREATE INDEX IX_PDPS_AsOf_Sector_Type ON dbo.ProjectDraftingProfitabilitySnapshot (AsOfDate, Sector, DraftingType);
END;
GO

/* columns added after the first run (2026-10-14, the Codex sober-second-thought pass) */
IF COL_LENGTH(N'dbo.ProjectDraftingProfitabilitySnapshot', N'PartnerEngHrs') IS NULL
    ALTER TABLE dbo.ProjectDraftingProfitabilitySnapshot ADD
        PartnerEngHrs        DECIMAL(12,2) NOT NULL CONSTRAINT DF_PDPS_PartnerEngHrs DEFAULT (0),
        PartnerEngAmtPosted  DECIMAL(18,2) NOT NULL CONSTRAINT DF_PDPS_PartnerEngAmtPosted DEFAULT (0),
        BilledBefore2019Cad  DECIMAL(18,2) NOT NULL CONSTRAINT DF_PDPS_BilledBefore2019Cad DEFAULT (0),
        FirstBillingPeriod   INT NULL;
GO

IF OBJECT_ID(N'dbo.DraftingTypeByYearSnapshot', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DraftingTypeByYearSnapshot
    (
        Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DraftingTypeByYearSnapshot PRIMARY KEY,
        AsOfDate           DATE          NOT NULL,
        [Year]             INT           NOT NULL,          -- calendar year: timesheet date for hours and cost, posting period for billings
        Sector             NVARCHAR(20)  NOT NULL CONSTRAINT DF_DTYS_Sector DEFAULT (''),
        DraftingType       NVARCHAR(20)  NOT NULL CONSTRAINT DF_DTYS_DraftingType DEFAULT (''),
        PartnerImputedRate DECIMAL(9,2)  NOT NULL,
        EngHrs             DECIMAL(12,2) NOT NULL,
        DraftHrs           DECIMAL(12,2) NOT NULL,
        BillableHrs        DECIMAL(12,2) NOT NULL,
        PartnerHrs         DECIMAL(12,2) NOT NULL,
        EngAmtPosted       DECIMAL(18,2) NOT NULL,
        DraftAmtPosted     DECIMAL(18,2) NOT NULL,
        LaborAmtPosted     DECIMAL(18,2) NOT NULL,
        PartnerAmtPosted   DECIMAL(18,2) NOT NULL,
        LaborCostCurrentAllStaff DECIMAL(18,2) NOT NULL,
        BilledCad          DECIMAL(18,2) NOT NULL,          -- PRSummaryMain posted in the year on jobs carrying the tag
        JobsWithHours      INT           NOT NULL,
        CreatedUtc         DATETIME2(0)  NOT NULL CONSTRAINT DF_DTYS_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_DraftingTypeByYearSnapshot UNIQUE (AsOfDate, [Year], Sector, DraftingType)
    );
END;
GO

/* Derived figures, one place. The report's margin population is IsFinished = 1 AND IsInWindow = 1.
   In-flight rows carry cost ahead of billing; rows outside the window carry billings before their labour.
   LaborCost (no suffix) is the report basis: posted staff amounts + partner hours at the imputed rate. */
CREATE OR ALTER VIEW dbo.vw_ProjectDraftingProfitability AS
SELECT
    s.*,
    CASE WHEN s.DraftingType = N'' THEN N'untagged' ELSE s.DraftingType END                        AS DraftingTypeLabel,
    f.IsFullyBilled,
    f.IsClosed,
    CASE WHEN f.IsFullyBilled = 1 OR f.IsClosed = 1 THEN 1 ELSE 0 END                               AS IsFinished,
    CASE WHEN s.BilledBefore2019Cad = 0 AND (s.OpenDate IS NULL OR s.OpenDate >= '2019-01-01') THEN 1 ELSE 0 END AS IsInWindow,
    f.IsFullyBilled                                                                                AS IsCompleted,   -- kept for the first edition's readers
    s.EngHrs + s.DraftHrs                                                                          AS ProductionHrs,
    YEAR(s.FirstHoursDate)                                                                         AS FirstHoursYear,
    d.LaborCost,
    d.DraftCost,
    d.EngCost,
    s.FeeBilledCad - d.LaborCost                                                                   AS Margin,
    CASE WHEN s.FeeBilledCad <> 0 THEN (s.FeeBilledCad - d.LaborCost) / s.FeeBilledCad END         AS MarginPct,
    s.FeeBilledCad - s.LaborAmtPosted                                                              AS MarginPartnersAtPayroll,
    s.FeeBilledCad - s.LaborCostCurrentAllStaff                                                    AS MarginCurrentAllStaff,
    s.FeeBilledCad - s.LaborCostCurrentActive                                                      AS MarginCurrentActive,
    CASE WHEN s.EngHrs + s.DraftHrs > 0 THEN s.FeeBilledCad / (s.EngHrs + s.DraftHrs) END          AS BilledPerProductionHr,
    CASE WHEN s.EngHrs + s.DraftHrs > 0 THEN d.LaborCost / (s.EngHrs + s.DraftHrs) END             AS CostPerProductionHr,
    CASE WHEN s.DraftHrs > 0 THEN s.FeeBilledCad / s.DraftHrs END                                  AS BilledPerDraftHr,
    CASE WHEN s.DraftHrs > 0 THEN d.DraftCost / s.DraftHrs END                                     AS DraftCostPerHr,
    CASE WHEN s.FeeBilledCad <> 0 THEN d.DraftCost / s.FeeBilledCad END                            AS DraftCostShareOfBilled,
    CASE WHEN s.EngHrs + s.DraftHrs > 0 THEN s.DraftHrs / (s.EngHrs + s.DraftHrs) END              AS DraftShareOfHrs
FROM dbo.ProjectDraftingProfitabilitySnapshot s
CROSS APPLY (SELECT
    CASE WHEN s.FeeCad > 0 AND s.FeeBilledCad >= 0.9 * s.FeeCad THEN 1 ELSE 0 END AS IsFullyBilled,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(s.Status, N'')))) <> N'A' THEN 1 ELSE 0 END AS IsClosed) f
CROSS APPLY (SELECT
    s.LaborAmtPosted - s.PartnerAmtPosted      + s.PartnerHrs      * s.PartnerImputedRate AS LaborCost,
    s.DraftAmtPosted - s.PartnerDraftAmtPosted + s.PartnerDraftHrs * s.PartnerImputedRate AS DraftCost,
    s.EngAmtPosted   - s.PartnerEngAmtPosted   + s.PartnerEngHrs   * s.PartnerImputedRate AS EngCost) d;
GO

CREATE OR ALTER PROCEDURE dbo.usp_SnapshotProjectDraftingProfitability
    @AsOfDate               DATE         = NULL,
    @UsdToCadRate           DECIMAL(6,4) = 1.36,
    @PartnerImputedCostRate DECIMAL(9,2) = 250
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @AsOfDate = COALESCE(@AsOfDate, CAST(SYSDATETIME() AS DATE));

    /* Everything is pulled into temp tables first, so a Deltek failure leaves the previous snapshot intact.
       The inner queries are static strings: partners are summed at 250 inside, and the INSERT re-prices
       their hours at @PartnerImputedCostRate. Amounts come back in project currency; the INSERT converts. */

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
            CAST(COALESCE(billed.BilledBefore2019, 0) AS DECIMAL(18,2)) AS BilledBefore2019,
            billed.FirstPeriod,
            CAST(L.EngHrs AS DECIMAL(12,2))                             AS EngHrs,
            CAST(L.DraftHrs AS DECIMAL(12,2))                           AS DraftHrs,
            CAST(L.BillableHrs AS DECIMAL(12,2))                        AS BillableHrs,
            CAST(L.PartnerHrs AS DECIMAL(12,2))                         AS PartnerHrs,
            CAST(L.PartnerHrsActive AS DECIMAL(12,2))                   AS PartnerHrsActive,
            CAST(L.PartnerDraftHrs AS DECIMAL(12,2))                    AS PartnerDraftHrs,
            CAST(L.PartnerEngHrs AS DECIMAL(12,2))                      AS PartnerEngHrs,
            CAST(L.EngAmt AS DECIMAL(18,2))                             AS EngAmt,
            CAST(L.DraftAmt AS DECIMAL(18,2))                           AS DraftAmt,
            CAST(L.LaborAmt AS DECIMAL(18,2))                           AS LaborAmt,
            CAST(L.PartnerAmt AS DECIMAL(18,2))                         AS PartnerAmt,
            CAST(L.PartnerDraftAmt AS DECIMAL(18,2))                    AS PartnerDraftAmt,
            CAST(L.PartnerEngAmt AS DECIMAL(18,2))                      AS PartnerEngAmt,
            CAST(L.EngCostCur AS DECIMAL(18,2))                         AS EngCostCur,
            CAST(L.DraftCostCur AS DECIMAL(18,2))                       AS DraftCostCur,
            CAST(L.CostCurAll AS DECIMAL(18,2))                         AS CostCurAll,
            CAST(L.CostCurActive AS DECIMAL(18,2))                      AS CostCurActive,
            L.FirstHrs,
            L.LastHrs
        FROM [C0000052267P_1_KOR00000000].dbo.PR pr
        LEFT JOIN [C0000052267P_1_KOR00000000].dbo.ProjectCustomTabFields pctf
               ON pctf.WBS1 = pr.WBS1 AND pctf.WBS2 = pr.WBS2 AND pctf.WBS3 = pr.WBS3
        LEFT JOIN (
            SELECT WBS1,
                   SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END)                                    AS FeeBilled,
                   SUM(CASE WHEN Period < 201901 THEN (CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) ELSE 0 END) AS BilledBefore2019,
                   MIN(Period)                                                                                      AS FirstPeriod
            FROM [C0000052267P_1_KOR00000000].dbo.PRSummaryMain
            GROUP BY WBS1
        ) billed ON billed.WBS1 = pr.WBS1
        LEFT JOIN (
            SELECT t.WBS1,
                SUM(CASE WHEN t.LaborCode IN (10,30) THEN h.Hrs ELSE 0 END)                                   AS EngHrs,
                SUM(CASE WHEN t.LaborCode = 20      THEN h.Hrs ELSE 0 END)                                   AS DraftHrs,
                SUM(CASE WHEN b.IsBillable = 1      THEN h.Hrs ELSE 0 END)                                   AS BillableHrs,
                SUM(CASE WHEN b.IsBillable = 1 AND t.Employee LIKE ''P%'' THEN h.Hrs ELSE 0 END)              AS PartnerHrs,
                SUM(CASE WHEN b.IsBillable = 1 AND t.Employee LIKE ''P%'' AND UPPER(COALESCE(ec.Status, ''A'')) = ''A'' THEN h.Hrs ELSE 0 END) AS PartnerHrsActive,
                SUM(CASE WHEN t.LaborCode = 20 AND t.Employee LIKE ''P%'' THEN h.Hrs ELSE 0 END)              AS PartnerDraftHrs,
                SUM(CASE WHEN t.LaborCode IN (10,30) AND t.Employee LIKE ''P%'' THEN h.Hrs ELSE 0 END)        AS PartnerEngHrs,
                SUM(CASE WHEN t.LaborCode IN (10,30) THEN h.Amt ELSE 0 END)                                   AS EngAmt,
                SUM(CASE WHEN t.LaborCode = 20      THEN h.Amt ELSE 0 END)                                   AS DraftAmt,
                SUM(CASE WHEN b.IsBillable = 1      THEN h.Amt ELSE 0 END)                                   AS LaborAmt,
                SUM(CASE WHEN b.IsBillable = 1 AND t.Employee LIKE ''P%'' THEN h.Amt ELSE 0 END)              AS PartnerAmt,
                SUM(CASE WHEN t.LaborCode = 20 AND t.Employee LIKE ''P%'' THEN h.Amt ELSE 0 END)              AS PartnerDraftAmt,
                SUM(CASE WHEN t.LaborCode IN (10,30) AND t.Employee LIKE ''P%'' THEN h.Amt ELSE 0 END)        AS PartnerEngAmt,
                SUM(CASE WHEN t.LaborCode IN (10,30) THEN h.Hrs * r.Rate ELSE 0 END)                          AS EngCostCur,
                SUM(CASE WHEN t.LaborCode = 20      THEN h.Hrs * r.Rate ELSE 0 END)                          AS DraftCostCur,
                SUM(CASE WHEN b.IsBillable = 1      THEN h.Hrs * r.Rate ELSE 0 END)                          AS CostCurAll,
                SUM(CASE WHEN b.IsBillable = 1 AND UPPER(COALESCE(ec.Status, ''A'')) = ''A'' THEN h.Hrs * r.Rate ELSE 0 END) AS CostCurActive,
                MIN(t.TransDate) AS FirstHrs,
                MAX(t.TransDate) AS LastHrs
            FROM [C0000052267P_1_KOR00000000].dbo.tkDetail t
            LEFT JOIN [C0000052267P_1_KOR00000000].dbo.EMCompany ec ON ec.Employee = t.Employee
            CROSS APPLY (SELECT COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) AS Hrs,
                                COALESCE(t.RegAmt,0)+COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0) AS Amt) h
            CROSS APPLY (SELECT CASE WHEN t.Employee LIKE ''P%'' THEN 250.0 ELSE COALESCE(ec.ProvCostRate,0) END AS Rate) r
            CROSS APPLY (SELECT CASE WHEN t.LaborCode NOT IN (70,80) AND t.WBS1 NOT LIKE ''[A-Z]%'' AND t.WBS1 NOT LIKE ''9[A-Z]%'' AND t.WBS1 NOT LIKE ''99%'' THEN 1 ELSE 0 END AS IsBillable) b
            WHERE t.Employee IS NOT NULL AND COALESCE(t.LineItemApprovalStatus, '''') <> ''R''
            GROUP BY t.WBS1
        ) L ON L.WBS1 = pr.WBS1
        WHERE pr.ChargeType = ''R''
          AND LTRIM(RTRIM(COALESCE(pr.WBS2, ''''))) = ''''
          AND LTRIM(RTRIM(COALESCE(pr.WBS3, ''''))) = ''''
          AND COALESCE(L.EngHrs, 0) + COALESCE(L.DraftHrs, 0) > 0
    ');

    /* hours and posted cost by timesheet year, split by currency so the INSERT can convert */
    SELECT *
    INTO #ylabor
    FROM OPENQUERY([DELTEK_VP], '
        SELECT YEAR(t.TransDate) AS Yr,
               LTRIM(RTRIM(COALESCE(pr.ProjectType, '''')))          AS Sector,
               LTRIM(RTRIM(COALESCE(pctf.CustDraftingType, ''''))) AS DraftingType,
               CASE WHEN LTRIM(RTRIM(COALESCE(pr.Org, ''''))) = ''USA'' THEN 1 ELSE 0 END AS IsUsa,
               CAST(SUM(CASE WHEN t.LaborCode IN (10,30) THEN h.Hrs ELSE 0 END) AS DECIMAL(12,2))          AS EngHrs,
               CAST(SUM(CASE WHEN t.LaborCode = 20      THEN h.Hrs ELSE 0 END) AS DECIMAL(12,2))          AS DraftHrs,
               CAST(SUM(CASE WHEN b.IsBillable = 1      THEN h.Hrs ELSE 0 END) AS DECIMAL(12,2))          AS BillableHrs,
               CAST(SUM(CASE WHEN b.IsBillable = 1 AND t.Employee LIKE ''P%'' THEN h.Hrs ELSE 0 END) AS DECIMAL(12,2)) AS PartnerHrs,
               CAST(SUM(CASE WHEN t.LaborCode IN (10,30) THEN h.Amt ELSE 0 END) AS DECIMAL(18,2))          AS EngAmt,
               CAST(SUM(CASE WHEN t.LaborCode = 20      THEN h.Amt ELSE 0 END) AS DECIMAL(18,2))          AS DraftAmt,
               CAST(SUM(CASE WHEN b.IsBillable = 1      THEN h.Amt ELSE 0 END) AS DECIMAL(18,2))          AS LaborAmt,
               CAST(SUM(CASE WHEN b.IsBillable = 1 AND t.Employee LIKE ''P%'' THEN h.Amt ELSE 0 END) AS DECIMAL(18,2)) AS PartnerAmt,
               CAST(SUM(CASE WHEN b.IsBillable = 1      THEN h.Hrs * r.Rate ELSE 0 END) AS DECIMAL(18,2)) AS CostCurAll,
               COUNT(DISTINCT t.WBS1) AS Jobs
        FROM [C0000052267P_1_KOR00000000].dbo.tkDetail t
        JOIN [C0000052267P_1_KOR00000000].dbo.PR pr
          ON pr.WBS1 = t.WBS1 AND pr.ChargeType = ''R''
         AND LTRIM(RTRIM(COALESCE(pr.WBS2, ''''))) = '''' AND LTRIM(RTRIM(COALESCE(pr.WBS3, ''''))) = ''''
        LEFT JOIN [C0000052267P_1_KOR00000000].dbo.ProjectCustomTabFields pctf
          ON pctf.WBS1 = pr.WBS1 AND pctf.WBS2 = pr.WBS2 AND pctf.WBS3 = pr.WBS3
        LEFT JOIN [C0000052267P_1_KOR00000000].dbo.EMCompany ec ON ec.Employee = t.Employee
        CROSS APPLY (SELECT COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) AS Hrs,
                            COALESCE(t.RegAmt,0)+COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0) AS Amt) h
        CROSS APPLY (SELECT CASE WHEN t.Employee LIKE ''P%'' THEN 250.0 ELSE COALESCE(ec.ProvCostRate,0) END AS Rate) r
        CROSS APPLY (SELECT CASE WHEN t.LaborCode NOT IN (70,80) AND t.WBS1 NOT LIKE ''[A-Z]%'' AND t.WBS1 NOT LIKE ''9[A-Z]%'' AND t.WBS1 NOT LIKE ''99%'' THEN 1 ELSE 0 END AS IsBillable) b
        WHERE t.Employee IS NOT NULL AND COALESCE(t.LineItemApprovalStatus, '''') <> ''R''
        GROUP BY YEAR(t.TransDate), LTRIM(RTRIM(COALESCE(pr.ProjectType, ''''))), LTRIM(RTRIM(COALESCE(pctf.CustDraftingType, ''''))),
                 CASE WHEN LTRIM(RTRIM(COALESCE(pr.Org, ''''))) = ''USA'' THEN 1 ELSE 0 END
    ');

    /* billings by posting-period year on the jobs carrying each tag */
    SELECT *
    INTO #ybilled
    FROM OPENQUERY([DELTEK_VP], '
        SELECT CAST(LEFT(CAST(sm.Period AS VARCHAR(8)), 4) AS INT)      AS Yr,
               LTRIM(RTRIM(COALESCE(pr.ProjectType, '''')))          AS Sector,
               LTRIM(RTRIM(COALESCE(pctf.CustDraftingType, ''''))) AS DraftingType,
               CASE WHEN LTRIM(RTRIM(COALESCE(pr.Org, ''''))) = ''USA'' THEN 1 ELSE 0 END AS IsUsa,
               CAST(SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS DECIMAL(18,2)) AS Billed
        FROM [C0000052267P_1_KOR00000000].dbo.PRSummaryMain sm
        JOIN [C0000052267P_1_KOR00000000].dbo.PR pr
          ON pr.WBS1 = sm.WBS1 AND pr.ChargeType = ''R''
         AND LTRIM(RTRIM(COALESCE(pr.WBS2, ''''))) = '''' AND LTRIM(RTRIM(COALESCE(pr.WBS3, ''''))) = ''''
        LEFT JOIN [C0000052267P_1_KOR00000000].dbo.ProjectCustomTabFields pctf
          ON pctf.WBS1 = pr.WBS1 AND pctf.WBS2 = pr.WBS2 AND pctf.WBS3 = pr.WBS3
        WHERE sm.Period >= 201901
        GROUP BY CAST(LEFT(CAST(sm.Period AS VARCHAR(8)), 4) AS INT), LTRIM(RTRIM(COALESCE(pr.ProjectType, ''''))),
                 LTRIM(RTRIM(COALESCE(pctf.CustDraftingType, ''''))), CASE WHEN LTRIM(RTRIM(COALESCE(pr.Org, ''''))) = ''USA'' THEN 1 ELSE 0 END
    ');

    BEGIN TRANSACTION;

    DELETE FROM dbo.ProjectDraftingProfitabilitySnapshot WHERE AsOfDate = @AsOfDate;
    INSERT INTO dbo.ProjectDraftingProfitabilitySnapshot
        (AsOfDate, Wbs1, ProjectName, Status, Org, Sector, DraftingType, Category, ConstructionType,
         OpenDate, FirstHoursDate, LastHoursDate, UsdToCadRate, PartnerImputedRate, FeeCad, FeeBilledCad,
         BilledBefore2019Cad, FirstBillingPeriod,
         EngHrs, DraftHrs, BillableHrs, PartnerHrs, PartnerDraftHrs, PartnerEngHrs,
         EngAmtPosted, DraftAmtPosted, LaborAmtPosted, PartnerAmtPosted, PartnerDraftAmtPosted, PartnerEngAmtPosted,
         EngCostCurrent, DraftCostCurrent, LaborCostCurrentAllStaff, LaborCostCurrentActive)
    SELECT
        @AsOfDate,
        LTRIM(RTRIM(j.WBS1)),
        COALESCE(j.Name, ''),
        j.Status,
        j.Org, j.Sector, j.DraftingType, j.Category, j.ConstructionType,
        CAST(j.OpenDate AS DATE), CAST(j.FirstHrs AS DATE), CAST(j.LastHrs AS DATE),
        x.Fx, @PartnerImputedCostRate,
        j.Fee       * x.Fx,
        j.FeeBilled * x.Fx,
        COALESCE(j.BilledBefore2019, 0) * x.Fx, j.FirstPeriod,
        COALESCE(j.EngHrs, 0), COALESCE(j.DraftHrs, 0), COALESCE(j.BillableHrs, 0),
        COALESCE(j.PartnerHrs, 0), COALESCE(j.PartnerDraftHrs, 0), COALESCE(j.PartnerEngHrs, 0),
        COALESCE(j.EngAmt, 0) * x.Fx, COALESCE(j.DraftAmt, 0) * x.Fx, COALESCE(j.LaborAmt, 0) * x.Fx,
        COALESCE(j.PartnerAmt, 0) * x.Fx, COALESCE(j.PartnerDraftAmt, 0) * x.Fx, COALESCE(j.PartnerEngAmt, 0) * x.Fx,
        /* current-rate figures were summed with partners at 250; re-price partner hours at the rate asked for */
        COALESCE(j.EngCostCur, 0)    - COALESCE(j.PartnerEngHrs, 0)    * 250 + COALESCE(j.PartnerEngHrs, 0)    * @PartnerImputedCostRate,
        COALESCE(j.DraftCostCur, 0)  - COALESCE(j.PartnerDraftHrs, 0)  * 250 + COALESCE(j.PartnerDraftHrs, 0)  * @PartnerImputedCostRate,
        COALESCE(j.CostCurAll, 0)    - COALESCE(j.PartnerHrs, 0)       * 250 + COALESCE(j.PartnerHrs, 0)       * @PartnerImputedCostRate,
        COALESCE(j.CostCurActive, 0) - COALESCE(j.PartnerHrsActive, 0) * 250 + COALESCE(j.PartnerHrsActive, 0) * @PartnerImputedCostRate
    FROM #jobs j
    CROSS APPLY (SELECT CASE WHEN j.Org = N'USA' THEN @UsdToCadRate ELSE 1.0 END AS Fx) x;

    DELETE FROM dbo.DraftingTypeByYearSnapshot WHERE AsOfDate = @AsOfDate;
    INSERT INTO dbo.DraftingTypeByYearSnapshot
        (AsOfDate, [Year], Sector, DraftingType, PartnerImputedRate,
         EngHrs, DraftHrs, BillableHrs, PartnerHrs, EngAmtPosted, DraftAmtPosted, LaborAmtPosted, PartnerAmtPosted,
         LaborCostCurrentAllStaff, BilledCad, JobsWithHours)
    SELECT
        @AsOfDate, k.Yr, k.Sector, k.DraftingType, @PartnerImputedCostRate,
        COALESCE(l.EngHrs, 0), COALESCE(l.DraftHrs, 0), COALESCE(l.BillableHrs, 0), COALESCE(l.PartnerHrs, 0),
        COALESCE(l.EngAmt, 0), COALESCE(l.DraftAmt, 0), COALESCE(l.LaborAmt, 0), COALESCE(l.PartnerAmt, 0),
        COALESCE(l.CostCurAll, 0) - COALESCE(l.PartnerHrs, 0) * 250 + COALESCE(l.PartnerHrs, 0) * @PartnerImputedCostRate,
        COALESCE(b.Billed, 0),
        COALESCE(l.Jobs, 0)
    FROM (SELECT Yr, Sector, DraftingType FROM #ylabor UNION SELECT Yr, Sector, DraftingType FROM #ybilled) k
    LEFT JOIN (
        SELECT Yr, Sector, DraftingType,
               SUM(EngHrs) AS EngHrs, SUM(DraftHrs) AS DraftHrs, SUM(BillableHrs) AS BillableHrs, SUM(PartnerHrs) AS PartnerHrs,
               SUM(EngAmt    * CASE WHEN IsUsa = 1 THEN @UsdToCadRate ELSE 1.0 END) AS EngAmt,
               SUM(DraftAmt  * CASE WHEN IsUsa = 1 THEN @UsdToCadRate ELSE 1.0 END) AS DraftAmt,
               SUM(LaborAmt  * CASE WHEN IsUsa = 1 THEN @UsdToCadRate ELSE 1.0 END) AS LaborAmt,
               SUM(PartnerAmt* CASE WHEN IsUsa = 1 THEN @UsdToCadRate ELSE 1.0 END) AS PartnerAmt,
               SUM(CostCurAll) AS CostCurAll,
               SUM(Jobs) AS Jobs
        FROM #ylabor GROUP BY Yr, Sector, DraftingType) l ON l.Yr = k.Yr AND l.Sector = k.Sector AND l.DraftingType = k.DraftingType
    LEFT JOIN (
        SELECT Yr, Sector, DraftingType, SUM(Billed * CASE WHEN IsUsa = 1 THEN @UsdToCadRate ELSE 1.0 END) AS Billed
        FROM #ybilled GROUP BY Yr, Sector, DraftingType) b ON b.Yr = k.Yr AND b.Sector = k.Sector AND b.DraftingType = k.DraftingType;

    COMMIT TRANSACTION;

    SELECT @AsOfDate AS AsOfDate,
           (SELECT COUNT(*) FROM dbo.ProjectDraftingProfitabilitySnapshot WHERE AsOfDate = @AsOfDate)                      AS JobRows,
           (SELECT COUNT(*) FROM dbo.ProjectDraftingProfitabilitySnapshot WHERE AsOfDate = @AsOfDate AND Sector = N'Res')  AS ResidentialRows,
           (SELECT COUNT(*) FROM dbo.DraftingTypeByYearSnapshot WHERE AsOfDate = @AsOfDate)                                AS YearRows;
END;
GO

/* Take today's snapshot now. Three Deltek pulls through DELTEK_VP: three to five minutes.
   The objects above print nothing; this is the only result set the script returns. */
EXEC dbo.usp_SnapshotProjectDraftingProfitability;
GO
