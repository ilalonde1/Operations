# Candidate SQL queries from the existing app

Auto-extracted by Vocabulary/extract_candidates.ps1.
Each entry shows the file, line, the C# method/comment context just above it, and the SQL string itself.
Mark items KEEP / DROP / REWRITE as you review; the keepers become the AI's vocabulary.

## Kor.Operations.App\Compensation\CompensationService.cs

### Line 132
```csharp

        var result = new List<EmployeeCompensationRow>();
        using var cn = factory.Create();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    e.Employee,
    e.FirstName,
    e.LastName,
    e.Title,
    ec.Region,
    ec.HireDate,
    COALESCE(ec.ProvBillRate, 0) AS BillingRate,
    COALESCE(ec.ProvCostRate, 0) AS CostRate
FROM [{catalog}].dbo.EMMain e
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = e.Employee
WHERE UPPER(COALESCE(ec.Status, 'A')) = 'A'
ORDER BY e.LastName, e.FirstName";

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var employeeId = GetTrimmed(r, 0);
            if (string.IsNullOrWhiteSpace(employeeId))
                continue;

            result.Add(new EmployeeCompensationRow
            {
                EmployeeId = employeeId,
                FirstName = GetTrimmed(r, 1),
                LastName = GetTrimmed(r, 2),
                Title = GetTrimmed(r, 3),
                Region = GetTrimmed(r, 4),
```

### Line 193
```csharp
        // FixedFeeBillExtAllocatable) are denominated in the project's
        // currency. Join PR for the master row's Org and FX-convert USA-org
        // dollars to CAD-equivalent so per-employee aggregates and the
        // firm-wide compensation pool size off a single currency. Hours
        // columns are currency-agnostic and stay raw.
        var fxRate = _usdToCadRate;
        cmd.CommandText = $@"
SELECT
    t.Employee,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs,
    SUM(CASE WHEN t.LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS OvtHrs,
    SUM(
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(prMaster.Org,'')))) = 'USA'
             THEN (COALESCE(t.RegAmt,0)+COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)) * ?
             ELSE  COALESCE(t.RegAmt,0)+COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)
        END
    ) AS LaborCost,
    SUM(
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(prMaster.Org,'')))) = 'USA'
             THEN (COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)) * ?
             ELSE  COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)
        END
    ) AS OvtCost,
    SUM(CASE WHEN COALESCE(pr.Fee, -1) = 0
              AND pr.WBS2 IS NOT NULL AND LTRIM(RTRIM(pr.WBS2)) <> ''
              AND pr.WBS3 IS NOT NULL AND LTRIM(RTRIM(pr.WBS3)) <> ''
             THEN
               CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(prMaster.Org,'')))) = 'USA'
                    THEN COALESCE(t.BillExt, 0) * ?
                    ELSE COALESCE(t.BillExt, 0)
               END
             ELSE 0 END) AS TmRevenue,
```

### Line 300
```csharp
        // before summing into the firmwide compensation pool. Without this the
        // pool sized off mixed CAD+USD totals.
        using var cn = factory.Create();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    COALESCE(SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE sm.Revenue END), 0) AS Amount
FROM [{catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{catalog}].dbo.PR pr
       ON pr.WBS1 = sm.WBS1
      AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period >= ? AND sm.Period <= ?
GROUP BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END";
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.Int, Value = startPeriod });
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.Int, Value = endPeriod });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var cadTotal = 0.0;
        var usaTotal = 0.0;
        while (r.Read())
        {
            var bucket = GetTrimmed(r, 0);
            var amt = GetDouble(r, 1);
            if (string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase))
                usaTotal += amt;
            else
                cadTotal += amt;
        }
        return cadTotal + (usaTotal * _usdToCadRate);
    }

    private Dictionary<(string Wbs1, string Wbs2, string Wbs3), double> LoadFixedFeeWbs3RatiosSync(CancellationToken ct)
    {
```

### Line 341
```csharp

        var result = new Dictionary<(string, string, string), double>();
        using var cn = factory.Create();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.WBS2,
    pr.WBS3,
    COALESCE(sm.RecognizedRevenue, 0) AS RecognizedRevenue,
    COALESCE(td.LaborBillExt, 0) AS LaborBillExt
FROM [{catalog}].dbo.PR pr
LEFT JOIN (
    SELECT WBS1, WBS2, WBS3, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) AS RecognizedRevenue
    FROM [{catalog}].dbo.PRSummaryMain
    GROUP BY WBS1, WBS2, WBS3
) sm ON sm.WBS1 = pr.WBS1 AND sm.WBS2 = pr.WBS2 AND sm.WBS3 = pr.WBS3
LEFT JOIN (
    SELECT WBS1, WBS2, WBS3, SUM(BillExt) AS LaborBillExt
    FROM [{catalog}].dbo.tkDetail
    WHERE COALESCE(LineItemApprovalStatus,'') <> 'R'
    GROUP BY WBS1, WBS2, WBS3
) td ON td.WBS1 = pr.WBS1 AND td.WBS2 = pr.WBS2 AND td.WBS3 = pr.WBS3
WHERE COALESCE(pr.Fee, 0) > 0
  AND pr.WBS2 IS NOT NULL AND LTRIM(RTRIM(pr.WBS2)) <> ''
  AND pr.WBS3 IS NOT NULL AND LTRIM(RTRIM(pr.WBS3)) <> ''";

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var wbs1 = GetTrimmed(r, 0);
            var wbs2 = GetTrimmed(r, 1);
            var wbs3 = GetTrimmed(r, 2);
```

### Line 402
```csharp
        cmd.CommandTimeout = SqlTimeouts.Batch;
        // BillExt is denominated in the project's currency. Join PR at the master
        // row for Org and FX-convert USA-org rows so per-employee allocations
        // sum in CAD-equivalent (downstream multiplies by a currency-neutral
        // ratio and aggregates across WBS3, which can span Orgs).
        var fxRate = _usdToCadRate;
        cmd.CommandText = $@"
SELECT
    t.Employee,
    t.WBS1,
    t.WBS2,
    t.WBS3,
    SUM(
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(prMaster.Org,'')))) = 'USA'
             THEN COALESCE(t.BillExt, 0) * ?
             ELSE COALESCE(t.BillExt, 0)
        END
    ) AS BillExt
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
LEFT JOIN [{catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1 AND pr.WBS2 = t.WBS2 AND pr.WBS3 = t.WBS3
LEFT JOIN [{catalog}].dbo.PR prMaster
       ON prMaster.WBS1 = t.WBS1
      AND (prMaster.WBS2 IS NULL OR LTRIM(RTRIM(prMaster.WBS2)) = '')
WHERE t.Employee IS NOT NULL
  AND t.TransDate IS NOT NULL
  AND t.TransDate >= ? AND t.TransDate < ?
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(pr.Fee, 0) > 0
  AND pr.WBS2 IS NOT NULL AND LTRIM(RTRIM(pr.WBS2)) <> ''
  AND pr.WBS3 IS NOT NULL AND LTRIM(RTRIM(pr.WBS3)) <> ''
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.Employee, t.WBS1, t.WBS2, t.WBS3";
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.Double, Value = fxRate });
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.DateTime, Value = startDate });
        cmd.Parameters.Add(new System.Data.Odbc.OdbcParameter { OdbcType = System.Data.Odbc.OdbcType.DateTime, Value = endExclusive });
```

## Kor.Operations.App\Crm\DeltekClientContextService.cs

### Line 266
```csharp
        string clientId,
        CancellationToken ct,
        out string clientName)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT ClientID, Name, Type, Status, Specialty, Market, Memo,
       ParentID, WebSite, PriorWork, Recommend, GovernmentAgency,
       Competitor, Employees, AnnualRevenue
FROM [{catalog}].dbo.Clendor
WHERE ClientID = ?";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        try
        {
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                clientName = string.Empty;
                return null;
            }

            clientName = GetString(r, 1) ?? clientId;
            return new DeltekCompanyFacts(
                ClientId: GetString(r, 0) ?? clientId,
                Type: GetString(r, 2),
                Status: GetString(r, 3),
                Specialty: GetString(r, 4),
                Market: GetString(r, 5),
                Memo: GetString(r, 6),
                ParentId: GetString(r, 7),
                Website: GetString(r, 8),
                PriorWork: IsYes(r, 9),
                Recommend: IsYes(r, 10),
                GovernmentAgency: IsYes(r, 11),
                Competitor: IsYes(r, 12),
```

### Line 316
```csharp
        CancellationToken ct)
    {
        // Bucket the LifetimeFee sum by Org so USA-org rows can be FX-converted to CAD-equiv
        // before being added to the headline. Filtered to the master row (WBS2 blank).
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
WITH ClientWbs AS (
    SELECT DISTINCT ar.WBS1
    FROM [{catalog}].dbo.AR ar
    WHERE ar.ClientID = ?
      AND LTRIM(RTRIM(ISNULL(ar.WBS1, ''))) <> ''
)
SELECT
    COUNT(DISTINCT pr.WBS1)                                                   AS ProjectCount,
    ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA'
                    THEN COALESCE(pr.Fee, 0) ELSE 0 END), 0)                  AS UsaFee,
    ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) <> 'USA'
                    THEN COALESCE(pr.Fee, 0) ELSE 0 END), 0)                  AS CadFee,
    MAX(pr.OpenDate)                                                          AS LatestStart
FROM ClientWbs cw
INNER JOIN [{catalog}].dbo.PR pr ON pr.WBS1 = cw.WBS1
WHERE pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '';";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return (0, 0m, null);
        }

        var usaFee = GetDecimal(r, 1) ?? 0m;
        var cadFee = GetDecimal(r, 2) ?? 0m;
        var lifetimeFee = cadFee + (usaFee * (decimal)_usdToCadRate);

        return (
            ProjectCount: r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0)),
```

### Line 359
```csharp
        string catalog,
        string clientId,
        CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
WITH ClientWbs AS (
    SELECT DISTINCT ar.WBS1
    FROM [{catalog}].dbo.AR ar
    WHERE ar.ClientID = ?
      AND LTRIM(RTRIM(ISNULL(ar.WBS1, ''))) <> ''
),
ProjectBilling AS (
    SELECT sm.WBS1,
           SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS FeeBilled
    FROM [{catalog}].dbo.PRSummaryMain sm
    WHERE sm.WBS2 IS NULL OR LTRIM(RTRIM(sm.WBS2)) = ''
    GROUP BY sm.WBS1
)
-- Returns ALL of the client's master projects so the lifetime fee tile reconciles
-- to Σ(visible rows). Previous TOP 50 cap silently truncated high-repeat clients.
SELECT pr.WBS1, pr.Name, pr.OpenDate, pr.Status, pr.Fee,
       COALESCE(pb.FeeBilled, 0) AS FeeBilled,
       pr.Org
FROM ClientWbs cw
INNER JOIN [{catalog}].dbo.PR pr ON pr.WBS1 = cw.WBS1
LEFT JOIN ProjectBilling pb ON pb.WBS1 = pr.WBS1
WHERE pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = ''
ORDER BY pr.OpenDate DESC;";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var rows = new List<DeltekProjectSummary>();
        var rate = (decimal)_usdToCadRate;
        while (r.Read())
        {
```

### Line 413
```csharp
        string catalog,
        string clientId,
        CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT TOP 50 ContactID, FirstName, LastName, Title, EMail, Phone,
       CellPhone, PrimaryInd, Rating
FROM [{catalog}].dbo.Contacts
WHERE ClientID = ?
  AND (ContactStatus IS NULL OR ContactStatus IN ('A', 'Active'))
ORDER BY PrimaryInd DESC, LastName, FirstName;";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var rows = new List<DeltekContactSummary>();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(new DeltekContactSummary(
                ContactId: GetString(r, 0) ?? "",
                FirstName: GetString(r, 1) ?? "",
                LastName: GetString(r, 2) ?? "",
                Title: GetString(r, 3),
                Email: GetString(r, 4),
                Phone: GetString(r, 5),
                CellPhone: GetString(r, 6),
                IsPrimary: IsYes(r, 7),
                Rating: GetString(r, 8)));
        }

        return rows;
    }

    private DeltekArSummary? LoadArSummary(
        OdbcConnection cn,
```

### Line 452
```csharp
        CancellationToken ct)
    {
        // Bucket by PR.Org so USA-org invoices (stored in USD) can be FX-converted to
        // CAD-equivalent before being aggregated into the client's outstanding/90+ tile.
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT
    Bucket,
    SUM(Outstanding)        AS Outstanding,
    SUM(Outstanding90Plus)  AS Outstanding90Plus,
    SUM(InvoiceCount)       AS InvoiceCount
FROM (
    SELECT
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
        COALESCE(ar.InvBalanceSourceCurrency,0) AS Outstanding,
        CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), CAST(GETDATE() AS date)) > 90
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END AS Outstanding90Plus,
        1 AS InvoiceCount
    FROM [{catalog}].dbo.AR ar
    LEFT JOIN [{catalog}].dbo.PR pr
      ON pr.WBS1 = ar.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
    WHERE ar.ClientID = ?
      AND ABS(COALESCE(ar.InvBalanceSourceCurrency, 0)) > 0.004
) x
GROUP BY Bucket;";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();

        var rate = (decimal)_usdToCadRate;
        decimal totalOutstanding = 0m;
        decimal totalOutstanding90 = 0m;
        var totalInvoiceCount = 0;
        var anyRow = false;
        while (r.Read())
        {
```

### Line 509
```csharp
        string catalog,
        string clientId,
        CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT TOP 10 ActivityID, Type, Subject, StartDate, Employee, WBS1
FROM [{catalog}].dbo.Activity
WHERE ClientID = ?
ORDER BY StartDate DESC, CreateDate DESC;";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var rows = new List<DeltekActivitySummary>();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(new DeltekActivitySummary(
                ActivityId: GetString(r, 0) ?? "",
                Type: GetString(r, 1),
                Subject: GetString(r, 2),
                StartDate: GetDate(r, 3),
                Employee: GetString(r, 4),
                Wbs1: GetString(r, 5)));
        }

        return rows;
    }

    private static string? GetString(IDataRecord r, int i)
    {
        if (r.IsDBNull(i)) return null;
        return Convert.ToString(r.GetValue(i))?.Trim();
    }

    private static DateTime? GetDate(IDataRecord r, int i)
```

## Kor.Operations.App\Crm\DeltekLookupService.cs

### Line 72
```csharp
        using var cn = _factory.Create();
        try { cn.Open(); }
        catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (DeltekLookup).", ex); }

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 15;
        cmd.CommandText = $@"
SELECT ClientID, Name, Type
FROM [{catalog}].dbo.Clendor
WHERE ClientInd = 'Y'
  AND (Status IS NULL OR Status <> 'I')
  AND LOWER(Name) LIKE ?";
        cmd.Parameters.Add(new OdbcParameter("@p", OdbcType.NVarChar, 100) { Value = prefix });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();

        var rows = new List<DeltekClientCandidate>(64);
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var clientId = DataReaderHelpers.GetTrimmed(r, 0);
            var name = DataReaderHelpers.GetTrimmed(r, 1);
            var type = r.IsDBNull(2) ? null : Convert.ToString(r.GetValue(2))?.Trim();
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var similarity = Similarity(normalizedQuery, NormalizeCompany(name));
            if (similarity >= 0.5)
            {
                rows.Add(new DeltekClientCandidate(clientId, name, type, similarity));
            }
        }

        return rows
            .OrderByDescending(c => c.SimilarityScore)
```

### Line 150
```csharp
        string? clientIdScope,
        int max,
        CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 15;
        cmd.CommandText = $@"
SELECT TOP {max} c.ContactID, c.ClientID, c.FirstName, c.LastName, c.EMail, c.Title
FROM [{catalog}].dbo.Contacts c
WHERE {(clientIdScope is null ? string.Empty : "c.ClientID = ? AND ")}LOWER(c.EMail) = ?
  AND (c.ContactStatus IS NULL OR c.ContactStatus IN ('A', 'Active'))
ORDER BY c.LastName, c.FirstName";
        if (clientIdScope is not null)
        {
            cmd.Parameters.Add(new OdbcParameter("@client", OdbcType.NVarChar, 32) { Value = clientIdScope });
        }

        cmd.Parameters.Add(new OdbcParameter("@email", OdbcType.NVarChar, 255) { Value = email });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        return ReadContactCandidates(r, _ => 1.0, ct);
    }

    private static IReadOnlyList<DeltekContactCandidate> FindContactsByName(
        OdbcConnection cn,
        string catalog,
        string normalizedName,
        string? clientIdScope,
        int max,
        CancellationToken ct)
    {
        var prefix = normalizedName.Substring(0, Math.Min(3, normalizedName.Length)) + "%";
        var sqlTop = Math.Max(max * 10, max);
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 15;
        cmd.CommandText = $@"
SELECT TOP {sqlTop} c.ContactID, c.ClientID, c.FirstName, c.LastName, c.EMail, c.Title
```

### Line 179
```csharp
        CancellationToken ct)
    {
        var prefix = normalizedName.Substring(0, Math.Min(3, normalizedName.Length)) + "%";
        var sqlTop = Math.Max(max * 10, max);
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 15;
        cmd.CommandText = $@"
SELECT TOP {sqlTop} c.ContactID, c.ClientID, c.FirstName, c.LastName, c.EMail, c.Title
FROM [{catalog}].dbo.Contacts c
WHERE {(clientIdScope is null ? string.Empty : "c.ClientID = ? AND ")}LOWER(COALESCE(c.FirstName, '') + ' ' + COALESCE(c.LastName, '')) LIKE ?
  AND (c.ContactStatus IS NULL OR c.ContactStatus IN ('A', 'Active'))";
        if (clientIdScope is not null)
        {
            cmd.Parameters.Add(new OdbcParameter("@client", OdbcType.NVarChar, 32) { Value = clientIdScope });
        }

        cmd.Parameters.Add(new OdbcParameter("@name", OdbcType.NVarChar, 120) { Value = prefix });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        return ReadContactCandidates(r, name => Similarity(normalizedName, NormalizePersonName(name)), ct)
            .Where(c => c.SimilarityScore >= 0.5)
            .OrderByDescending(c => c.SimilarityScore)
            .ThenBy(c => c.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    private static IReadOnlyList<DeltekContactCandidate> ReadContactCandidates(
        OdbcDataReader r,
        Func<string, double> score,
        CancellationToken ct)
    {
        var rows = new List<DeltekContactCandidate>();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var contactId = DataReaderHelpers.GetTrimmed(r, 0);
```

## Kor.Operations.App\Financials\BillingManagerReportViewModel.cs

### Line 616
```csharp
            cn.Open();

            foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, Period, SUM(COALESCE(Billed, 0)) AS Billed
FROM [{catalog}].dbo.PRSummaryMain
WHERE WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1, Period;";
                ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r   = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1   = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                    var period = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                    if (wbs1.Length == 0 || period.Length == 0) continue;
                    var fx = fxByWbs1.TryGetValue(wbs1, out var rate) ? rate : 1.0;
                    var billed = ExecutiveSummaryLoaderSupport.GetDouble(r, 2) * fx;
                    if (Math.Abs(billed) < AnalyticsThresholds.RoundingDollarFloor) continue;

                    if (!byWbsPeriod.TryGetValue(wbs1, out var pmap))
                        byWbsPeriod[wbs1] = pmap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    pmap[period] = billed;
                    allPeriods.Add(period);
                }
            }

            var sorted = allPeriods.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            return (sorted, byWbsPeriod);
        }

        private static string FormatPeriodLabel(string period)
```

## Kor.Operations.App\Financials\DeltekSchemaValidator.cs

### Line 109
```csharp
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Pull every column on every expected table once, then bucket in memory.
            // Cheaper than N round-trips and keeps the SQL trivial / injection-safe
            // (table names come from a hard-coded constant set, not user input).
            var inList = string.Join(",", expectedTables.Select(t => $"'{t.Replace("'", "''")}'"));
            cmd.CommandText = $@"
SELECT TABLE_NAME, COLUMN_NAME
FROM [{resolved}].INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ({inList});";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = await Task.Run(() => cmd.ExecuteReader(), ct).ConfigureAwait(false);
            while (await Task.Run(() => r.Read(), ct).ConfigureAwait(false))
            {
                var t = (r.GetValue(0)?.ToString() ?? string.Empty).Trim();
                var col = (r.GetValue(1)?.ToString() ?? string.Empty).Trim();
                if (t.Length > 0 && col.Length > 0) found.Add($"{t}.{col}");
            }

            var missing = ExpectedColumns
                .Select(c => $"{c.Table}.{c.Column}")
                .Where(qn => !found.Contains(qn))
                .ToList();

            if (missing.Count > 0)
            {
                Log.ForContext(typeof(DeltekSchemaValidator)).Warning(
                    "Deltek schema drift detected on catalog {Catalog}: missing {MissingCount} columns: {Missing}",
                    catalog, missing.Count, string.Join(", ", missing));
            }

            return missing;
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(DeltekSchemaValidator)).Warning(ex,
```

## Kor.Operations.App\Financials\FinancialsService.cs

### Line 326
```csharp
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed.", ex); }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.Name,
    pr.Status,
    pr.ProjMgr,
    em.FirstName,
    em.LastName,
    pctf.CustProjectPhase AS Phase,
    pctf.CustActualGFA AS GFA,
    pr.Fee,
    pctf.CustWatchlist,
    pctf.CustDraftingManager,
    em2.FirstName AS DmFirstName,
    em2.LastName AS DmLastName,
    pr.Principal,
    em3.FirstName AS BmFirstName,
    em3.LastName AS BmLastName,
    pctf.CustConstructionType,
    pctf.CustProjectCategory,
    pctf.CustDraftingType,
    pr.Org
 FROM [{catalog}].dbo.PR pr
 LEFT JOIN [{catalog}].dbo.ProjectCustomTabFields pctf
     ON pctf.WBS1 = pr.WBS1
    AND (pctf.WBS2 IS NULL OR LTRIM(RTRIM(pctf.WBS2)) = '')
 LEFT JOIN [{catalog}].dbo.EMMain em
     ON em.Employee = pr.ProjMgr
LEFT JOIN [{catalog}].dbo.EMMain em2
    ON em2.Employee = pctf.CustDraftingManager
LEFT JOIN [{catalog}].dbo.EMMain em3
```

### Line 407
```csharp
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (fee billed).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) AS FeeBilled
FROM [{catalog}].dbo.PRSummaryMain
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (!string.IsNullOrWhiteSpace(wbs1))
                        result[wbs1] = GetDouble(r, 1);
                }
            }
            return result;
        }

        private static Dictionary<string, double> LoadUnpostedFeeBilledSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (unposted fee billed).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                var phInv = MakeInListPlaceholders(chunk.Count);
```

### Line 446
```csharp
                // rolled up to PRSummaryMain. The invoiced side MUST come from
                // LedgerAR (TransType='IN', Daler's canonical 4001/4003/4210/4220/4240
                // account list), NOT from AR.InvBalanceSourceCurrency: AR's open balance
                // goes to 0 once an invoice is collected, so at KOR's ~3-month posting
                // lag — where most invoices are paid before they post — using AR
                // silently drops the bulk of legitimately unposted billings.
                cmd.CommandText = $@"
SELECT WBS1, SUM(UnpostedAmt) AS UnpostedFeeBilled
FROM (
    SELECT
        invP.WBS1,
        invP.Period,
        invP.InvAmt - COALESCE(prP.PostedAmt, 0) AS UnpostedAmt
    FROM (
        SELECT WBS1, Period, SUM(-Amount) AS InvAmt
        FROM [{catalog}].dbo.LedgerAR
        WHERE WBS1 IN ({phInv})
          AND TransType = 'IN'
          AND LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ('4001', '4003', '4210', '4220', '4240')
        GROUP BY WBS1, Period
    ) invP
    LEFT JOIN (
        SELECT WBS1, Period,
               SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue, 0) END) AS PostedAmt
        FROM [{catalog}].dbo.PRSummaryMain
        WHERE WBS1 IN ({phPr})
        GROUP BY WBS1, Period
    ) prP
        ON prP.WBS1 = invP.WBS1 AND prP.Period = invP.Period
    WHERE invP.InvAmt - COALESCE(prP.PostedAmt, 0) > 0
) gap
GROUP BY WBS1;";
                AddInListParameters(cmd, chunk);
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
```

### Line 498
```csharp
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (hourly revenue).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT sm.WBS1, SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS HourlyRevenue
FROM [{catalog}].dbo.PRSummaryMain sm
INNER JOIN [{catalog}].dbo.PR pr
    ON pr.WBS1 = sm.WBS1 AND pr.WBS2 = sm.WBS2 AND pr.WBS3 = sm.WBS3
WHERE sm.WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND pr.Fee = 0
  AND pr.WBS2 IS NOT NULL AND LTRIM(RTRIM(pr.WBS2)) <> ''
  AND pr.WBS3 IS NOT NULL AND LTRIM(RTRIM(pr.WBS3)) <> ''
GROUP BY sm.WBS1
HAVING SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) > 0;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (!string.IsNullOrWhiteSpace(wbs1))
                        result[wbs1] = GetDouble(r, 1);
                }
            }
            return result;
        }

        private static Dictionary<(string Wbs1, int LaborCode), double> LoadHoursByLaborSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<(string, int), double>();
            using var cn = factory.Create();
            try { cn.Open(); }
```

### Line 534
```csharp
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (hours by labor).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, LaborCode, SUM(COALESCE(RegHrs,0) + COALESCE(OvtHrs,0) + COALESCE(SpecialOvtHrs,0)) AS Hrs
FROM [{catalog}].dbo.tkDetail
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND COALESCE(LineItemApprovalStatus,'') <> 'R'
GROUP BY WBS1, LaborCode;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    var laborObj = r.IsDBNull(1) ? null : r.GetValue(1);
                    if (string.IsNullOrWhiteSpace(wbs1) || laborObj == null) continue;
                    if (TryParseLaborCode(laborObj, out var laborCode))
                        result[(wbs1, laborCode)] = GetDouble(r, 2);
                }
            }
            return result;
        }

        private static Dictionary<(string Wbs1, int LaborCode), double> LoadCostByLaborSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<(string, int), double>();
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (cost by labor).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
```

### Line 567
```csharp
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (cost by labor).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, LaborCode,
       SUM(COALESCE(RegAmt,0) + COALESCE(OvtAmt,0) + COALESCE(SpecialOvtAmt,0)) AS Cost
FROM [{catalog}].dbo.tkDetail
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND COALESCE(LineItemApprovalStatus,'') <> 'R'
GROUP BY WBS1, LaborCode;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    var laborObj = r.IsDBNull(1) ? null : r.GetValue(1);
                    if (string.IsNullOrWhiteSpace(wbs1) || laborObj == null) continue;
                    if (TryParseLaborCode(laborObj, out var laborCode))
                        result[(wbs1, laborCode)] = GetDouble(r, 2);
                }
            }
            return result;
        }

        private static Dictionary<string, Dictionary<string, double>> LoadPrLaborSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (PR labor).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
```

### Line 601
```csharp
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (PR labor).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, LaborID, SUM(COALESCE(EstimateHrs,0)) AS BudgetHrs
FROM [{catalog}].dbo.PRLabor
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1, LaborID;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1    = GetTrimmed(r, 0);
                    var laborId = GetTrimmed(r, 1);
                    if (string.IsNullOrWhiteSpace(wbs1) || string.IsNullOrWhiteSpace(laborId)) continue;
                    if (!result.TryGetValue(wbs1, out var inner))
                        result[wbs1] = inner = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    inner[laborId] = GetDouble(r, 2);
                }
            }
            return result;
        }

        private static Dictionary<string, double> LoadApSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (AP).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
```

### Line 634
```csharp
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (AP).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, SUM(COALESCE(Amount, 0)) AS ApTotal
FROM [{catalog}].dbo.apDetail
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (!string.IsNullOrWhiteSpace(wbs1))
                        result[wbs1] = GetDouble(r, 1);
                }
            }
            return result;
        }

        /// <summary>
        /// Resolves each WBS1 to its client (ClientID + Name) via the AR table.
        /// A project's client is the most recent ClientID found on its invoices.
        /// Projects with no invoices return an empty entry (handled as "(unknown)" in the rollup).
        /// </summary>
        private static Dictionary<string, (string ClientId, string ClientName)> LoadClientLookupSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (clients).", ex); }
```

### Line 670
```csharp
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (clients).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                // Pick the most recent ClientID per WBS1 from AR, then resolve the display name from Clendor.
                cmd.CommandText = $@"
SELECT latest.WBS1, latest.ClientID, COALESCE(cc.Name, '') AS ClientName
FROM (
    SELECT ar.WBS1, ar.ClientID,
           ROW_NUMBER() OVER (PARTITION BY ar.WBS1
                              ORDER BY COALESCE(ar.InvoiceDate, ar.DueDate) DESC) AS rn
    FROM [{catalog}].dbo.AR ar
    WHERE ar.WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
      AND ar.ClientID IS NOT NULL
      AND LTRIM(RTRIM(ar.ClientID)) <> ''
) latest
LEFT JOIN [{catalog}].dbo.Clendor cc ON cc.ClientID = latest.ClientID
WHERE latest.rn = 1;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (string.IsNullOrWhiteSpace(wbs1)) continue;
                    var clientId = GetTrimmed(r, 1);
                    var clientName = GetTrimmed(r, 2);
                    result[wbs1] = (clientId, clientName);
                }
            }
            return result;
        }

        /// <summary>
        /// Loads lifetime client analytics across ALL projects (active + closed), independent of
```

### Line 719
```csharp
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (client portfolio).", ex); }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // One row per project: every project ever, joined to its most recent client via AR,
            // billed totals from PRSummaryMain, AR outstanding + 90+ aging from AR.
            cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.Name,
    pr.Status,
    pctf.CustProjectPhase AS Phase,
    pr.Fee,
    pr.OpenDate,
    pr.CloseDate,
    ISNULL(billed.FeeBilled, 0) AS FeeBilled,
    ISNULL(unposted.UnpostedFeeBilled, 0) AS UnpostedFeeBilled,
    ISNULL(arSum.Outstanding, 0) AS Outstanding,
    ISNULL(arSum.Aged90Plus, 0) AS Aged90Plus,
    latest.ClientID,
    COALESCE(cc.Name, '') AS ClientName,
    ISNULL(hourly.HourlyRevenue, 0) AS HourlyRevenue,
    pr.Org
FROM [{catalog}].dbo.PR pr
LEFT JOIN [{catalog}].dbo.ProjectCustomTabFields pctf
    ON pctf.WBS1 = pr.WBS1
   AND (pctf.WBS2 IS NULL OR LTRIM(RTRIM(pctf.WBS2)) = '')
LEFT JOIN (
    SELECT WBS1, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) AS FeeBilled
    FROM [{catalog}].dbo.PRSummaryMain
    GROUP BY WBS1
) billed ON billed.WBS1 = pr.WBS1
LEFT JOIN (
    -- Per-period unposted-billings reconciliation for client portfolio.
    -- Per (WBS1, Period): unposted = MAX(0, LedgerAR_invoiced - PRSummaryMain_billed).
    -- Invoiced side from LedgerAR (TransType='IN', Daler's canonical revenue accounts)
    -- so paid-but-not-yet-posted invoices stay visible — see LoadUnpostedFeeBilledSync.
```

### Line 913
```csharp
            // 1) Try to load the period→date calendar mapping (best-effort; fall back to YYYYMM parsing if missing).
            var calendar = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var calCmd = cn.CreateCommand();
                calCmd.CommandTimeout = SqlTimeouts.Batch;
                calCmd.CommandText = $@"SELECT Period, StartDate FROM [{catalog}].dbo.CFGAcctngCalendarData;";
                using var calReg = ct.Register(() => { try { calCmd.Cancel(); } catch { } });
                using var cr = calCmd.ExecuteReader();
                while (cr.Read())
                {
                    var p = GetTrimmed(cr, 0);
                    if (string.IsNullOrEmpty(p) || cr.IsDBNull(1)) continue;
                    var d = Convert.ToDateTime(cr.GetValue(1), CultureInfo.InvariantCulture);
                    calendar[p] = new DateTime(d.Year, d.Month, 1);
                }
            }
            catch
            {
                // Calendar table missing or inaccessible — fall back to YYYYMM parsing only.
            }

            // 2) Sum revenue by period and Org bucket across all projects, then FX-convert USA
            //    rows to CAD-equivalent before adding to the period total. Without this, the
            //    forecast trailing-12 / baseline / slope / seasonality all run on a CAD+USD mix.
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    sm.Period,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS Revenue
FROM [{catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
GROUP BY sm.Period, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

```

### Line 934
```csharp

            // 2) Sum revenue by period and Org bucket across all projects, then FX-convert USA
            //    rows to CAD-equivalent before adding to the period total. Without this, the
            //    forecast trailing-12 / baseline / slope / seasonality all run on a CAD+USD mix.
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    sm.Period,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS Revenue
FROM [{catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
GROUP BY sm.Period, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var period = GetTrimmed(r, 0);
                if (string.IsNullOrEmpty(period)) continue;
                var bucket = GetTrimmed(r, 1);
                var rawRevenue = GetDouble(r, 2);
                if (rawRevenue == 0) continue;
                var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
                var revenue = rawRevenue * fx;

                DateTime monthStart;
                if (calendar.TryGetValue(period, out var calMonth))
                {
                    monthStart = calMonth;
                }
                else if (period.Length == 6
                         && int.TryParse(period.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
                         && int.TryParse(period.Substring(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)
```

### Line 988
```csharp
            var fallbackEndMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
            var endMonth = fallbackEndMonth;
            try
            {
                using var maxCmd = cn.CreateCommand();
                maxCmd.CommandTimeout = SqlTimeouts.Batch;
                maxCmd.CommandText = $@"SELECT MAX(Period) FROM [{catalog}].dbo.PRSummaryMain;";
                using var maxReg = ct.Register(() => { try { maxCmd.Cancel(); } catch { } });
                var maxPeriodObj = maxCmd.ExecuteScalar();
                if (maxPeriodObj != null && maxPeriodObj != DBNull.Value)
                {
                    var maxPeriod = Convert.ToString(maxPeriodObj, CultureInfo.InvariantCulture)?.Trim();
                    if (TryParseDeltekPeriod(maxPeriod, out var parsedPeriod))
                    {
                        endMonth = parsedPeriod;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unexpected MAX(Period) value '{maxPeriod ?? "<null>"}'.");
                    }
                }
                else
                {
                    throw new InvalidOperationException("MAX(Period) returned NULL.");
                }
            }
            catch (Exception ex)
            {
                var logger = global::Kor.Operations.Services.AppServices.GetOptional<global::Microsoft.Extensions.Logging.ILoggerFactory>()
                    ?.CreateLogger(nameof(FinancialsService));
                if (logger != null)
                {
                    global::Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                        logger,
                        ex,
                        "Revenue history max-period probe failed; falling back to previous full calendar month.");
                }
```

### Line 1044
```csharp
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (max posted period).", ex); }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"SELECT MAX(Period) FROM [{catalog}].dbo.PRSummaryMain;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var maxPeriodObj = cmd.ExecuteScalar();
            if (maxPeriodObj == null || maxPeriodObj == DBNull.Value)
                return null;

            var maxPeriod = Convert.ToString(maxPeriodObj, CultureInfo.InvariantCulture)?.Trim();
            return TryParseDeltekPeriod(maxPeriod, out var parsedPeriod) ? parsedPeriod : null;
        }

        private static bool TryParseDeltekPeriod(string? period, out DateTime monthStart)
        {
            monthStart = default;
            if (string.IsNullOrWhiteSpace(period) || period.Length != 6)
                return false;

            if (!int.TryParse(period.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                || !int.TryParse(period.Substring(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
                || month < 1 || month > 12 || year < 1990 || year > 2100)
            {
                return false;
            }

            monthStart = new DateTime(year, month, 1);
            return true;
        }

        internal static FinancialsHeadlineKpis ComputeHeadline(List<FinancialsProjectRow> rows)
            => FinancialsHeadlineCalculator.Compute(rows);

        private static Dictionary<string, (int Total, int LastMonth)> LoadInspectionCountsSync(
```

### Line 1087
```csharp
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (inspections).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1,
    COUNT(*) AS TotalInspections,
    SUM(CASE WHEN TransDate >= ? AND TransDate < ? THEN 1 ELSE 0 END) AS LastMonthInspections
FROM [{catalog}].dbo.tkDetail
WHERE LaborCode = {LaborCodes.Inspection}
  AND WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND COALESCE(LineItemApprovalStatus,'') <> 'R'
GROUP BY WBS1;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = monthStart });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = monthEnd });
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (!string.IsNullOrWhiteSpace(wbs1))
                        result[wbs1] = ((int)GetDouble(r, 1), (int)GetDouble(r, 2));
                }
            }
            return result;
        }

        private double CalcBudget(double fee, double rate, double u3)
        {
            // Fee-based estimation using a single configurable target rate.
            // Band-specific rates were attempted but the source data ($/hr by fee band)
            // was skewed by active projects with incomplete hours, producing budgets
            // that were far too small. The single rate is more conservative and reliable.
```

### Line 1182
```csharp
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (peers).", ex); }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.Fee,
    pctf.CustProjectPhase,
    pctf.CustConstructionType,
    ISNULL(labor.EngHrs, 0) AS EngHrs,
    ISNULL(labor.DraftHrs, 0) AS DraftHrs,
    pctf.CustProjectCategory
FROM [{catalog}].dbo.PR pr
LEFT JOIN [{catalog}].dbo.ProjectCustomTabFields pctf
    ON pctf.WBS1 = pr.WBS1
   AND (pctf.WBS2 IS NULL OR LTRIM(RTRIM(pctf.WBS2)) = '')
LEFT JOIN (
    SELECT WBS1,
        SUM(CASE WHEN LaborCode IN ({LaborCodes.Engineering}, {LaborCodes.Checking}) THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS EngHrs,
        SUM(CASE WHEN LaborCode = {LaborCodes.Drafting} THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS DraftHrs
    FROM [{catalog}].dbo.tkDetail
    WHERE WBS1 NOT LIKE '[A-Z]%'
      AND WBS1 NOT LIKE '9[A-Z]%'
      AND WBS1 NOT LIKE '99%'
      AND COALESCE(LineItemApprovalStatus,'') <> 'R'
    GROUP BY WBS1
) labor ON labor.WBS1 = pr.WBS1
WHERE (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
  AND UPPER(LTRIM(RTRIM(pr.Status))) NOT IN ('A', 'ACTIVE')
  AND pr.Fee > 0
  AND pr.WBS1 NOT LIKE '[A-Z]%'
  AND pr.WBS1 NOT LIKE '9[A-Z]%'
  AND pr.WBS1 NOT LIKE '99%'
  AND (ISNULL(labor.EngHrs, 0) + ISNULL(labor.DraftHrs, 0)) >= 50";
```

## Kor.Operations.App\Financials\Loaders\ArLoader.cs

### Line 61
```csharp
    {
        var asOf = DateTime.Today.Date;
        // Bucket by Org (joined from PR via WBS1) so USA balances can be FX-converted
        // before being summed with CAD. Falls back to CAD bucket when Org is null/missing.
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    Bucket,
    SUM(InvBalance)         AS Outstanding,
    SUM(InvBalanceOver60)   AS Over60
FROM (
    SELECT
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
        COALESCE(ar.InvBalanceSourceCurrency,0) AS InvBalance,
        CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) > 60
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END AS InvBalanceOver60
    FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.AR ar
    LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
      ON pr.WBS1 = ar.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
    WHERE ABS(COALESCE(ar.InvBalanceSourceCurrency,0)) > 0.004
) x
GROUP BY Bucket;";
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });

        var cadOutstanding = 0.0;
        var usaOutstanding = 0.0;
        var cadOver60 = 0.0;
        var usaOver60 = 0.0;

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            var outstanding = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
            var over60 = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
```

### Line 133
```csharp
                : "WHERE ";
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Group by WBS1 + Org so USA-org rows can be FX-converted to CAD-equivalent.
            // Without this, the scoped AR drilldown sums in source currency while the
            // firmwide AR headline (LoadFirmwideArTotals) is CAD-equiv — they'd diverge.
            cmd.CommandText = $@"
SELECT
    ar.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(ar.InvBalanceSourceCurrency,0)) AS TotalOutstanding,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) <= 30
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS CurrentAmt,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) BETWEEN 31 AND 60
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt31To60,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) BETWEEN 61 AND 90
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt61To90,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(ar.DueDate, ar.InvoiceDate), ?) > 90
             THEN COALESCE(ar.InvBalanceSourceCurrency,0) ELSE 0 END) AS Amt90Plus,
    MIN(COALESCE(ar.InvoiceDate, ar.DueDate)) AS OldestInvoiceDate,
    MAX(COALESCE(pr.Name,'')) AS ProjectName,
    MAX(COALESCE(pr.ProjMgr,'')) AS ProjMgr,
    MAX(COALESCE(em.FirstName,'')) AS PmFirstName,
    MAX(COALESCE(em.LastName,'')) AS PmLastName
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.AR ar
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = ar.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.EMMain em
  ON em.Employee = pr.ProjMgr
{inWbs1}ABS(COALESCE(ar.InvBalanceSourceCurrency,0)) > 0.004
GROUP BY ar.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });
            if (chunk != null) ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
```

### Line 226
```csharp
            using var cmdDetail = cn.CreateCommand();
            cmdDetail.CommandTimeout = SqlTimeouts.Batch;
            // Pull invoice number + client identity alongside the open balance so
            // the AR drilldown can answer "which Deltek invoice is this?" and
            // "who do I call?" without a second lookup. Clendor is Deltek's
            // combined client/vendor master keyed by ClientID.
            cmdDetail.CommandText = $@"
SELECT
    ar.WBS1,
    COALESCE(ar.Invoice,'') AS Invoice,
    COALESCE(ar.ClientID,'') AS ClientID,
    COALESCE(cc.Name,'') AS ClientName,
    ar.InvoiceDate,
    ar.DueDate,
    COALESCE(ar.InvBalanceSourceCurrency,0) AS OpenBalance,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    COALESCE(pr.Name,'') AS ProjectName,
    COALESCE(pr.ProjMgr,'') AS ProjMgr,
    COALESCE(em.FirstName,'') AS PmFirstName,
    COALESCE(em.LastName,'') AS PmLastName
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.AR ar
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = ar.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.EMMain em
  ON em.Employee = pr.ProjMgr
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.Clendor cc
  ON cc.ClientID = ar.ClientID
{inWbs1}ABS(COALESCE(ar.InvBalanceSourceCurrency,0)) > 0.004;";
            if (chunk != null) ExecutiveSummaryLoaderSupport.AddInListParameters(cmdDetail, chunk);

            using var regDetail = ct.Register(() => { try { cmdDetail.Cancel(); } catch { } });
            using var rd = cmdDetail.ExecuteReader();
            while (rd.Read())
            {
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(rd, 0);
                if (w.Length == 0) continue;
                var invoice = ExecutiveSummaryLoaderSupport.GetTrimmed(rd, 1);
```

## Kor.Operations.App\Financials\Loaders\CashLoader.cs

### Line 102
```csharp
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;

            var clauses = string.Join(" OR ", Enumerable.Repeat("(Account = ? AND Org = ?)", chunk.Count));

            cmd.CommandText = $@"
SELECT Period, Account, Org, SUM(COALESCE(Amount,0)) AS Amt
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.GLSummary
WHERE Period <= ?
  AND ({clauses})
GROUP BY Period, Account, Org;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = targetPeriod });
            foreach (var b in chunk)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Account });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Org });
            }

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();

                var period = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (period.Length != 6 || !period.All(char.IsDigit))
                    continue;

                var acct = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                var org = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 2);
                var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 3);

                var match = chunk.FirstOrDefault(x =>
                    string.Equals(x.Account, acct, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Org, org, StringComparison.OrdinalIgnoreCase));
```

### Line 198
```csharp

    private static List<BankAcct> LoadBankAccounts(OdbcConnection cn, FinancialsOptions financialsOptions, CancellationToken ct)
    {
        var whitelist = ParseAccountWhitelist(financialsOptions);
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT Company, Account, Org
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.CFGBanks
WHERE COALESCE(Account,'') <> '';";

        var list = new List<BankAcct>(64);
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();

            var company = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            var account = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
            var org = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 2);

            if (company.Length == 0 || account.Length == 0)
                continue;

            if (string.IsNullOrWhiteSpace(org))
                org = company;

            if (whitelist.Count > 0 && !MatchesAccountSet(account, whitelist))
                continue;

            list.Add(new BankAcct(company, account, org));
        }

        return list
            .GroupBy(b => string.Join("|", b.Company, b.Account, b.Org), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
```

### Line 330
```csharp
        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(accts, 40))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;

            var clauses = string.Join(" OR ", Enumerable.Repeat("(Account = ? AND Org = ?)", chunk.Count));
            cmd.CommandText = $@"
SELECT MAX(Period) AS MaxPeriod
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.GLSummary
WHERE Period <= ? AND ({clauses});";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = todayPeriod });
            foreach (var b in chunk)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Account });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = b.Org });
            }

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var v = cmd.ExecuteScalar();
            var p = (v == null || v == DBNull.Value) ? string.Empty : (Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty);
            p = p.Trim();

            if (p.Length > 0 && string.CompareOrdinal(p, latest) > 0)
                latest = p;
        }

        return latest;
    }
}
```

## Kor.Operations.App\Financials\Loaders\FirmHealthLoader.cs

### Line 95
```csharp
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        // Account codes at KOR are stored with a ".00" suffix (e.g. '4001.00'),
        // and other Deltek installations may store them without padding or with
        // trailing spaces. Match on the 4-char prefix so the predicate works
        // regardless of catalog-level formatting choices.
        cmd.CommandText = $@"
SELECT
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(-Amount) AS Invoiced
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.LedgerAR
WHERE TransType = 'IN'
  AND LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ('4001', '4003', '4210', '4220', '4240')
  AND Period >= ?
GROUP BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = sincePeriodInt });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var cadTotal = 0.0;
        var usaTotal = 0.0;
        while (r.Read())
        {
            var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
            if (string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase))
                usaTotal += amt;
            else
                cadTotal += amt;
        }
        return cadTotal + (usaTotal * usdToCadRate);
    }

    /// <summary>
    /// Direct Labor Cost: trailing-12mo SUM(RegAmt+OvtAmt+SpecialOvtAmt) for
    /// timesheet entries against direct (non-overhead) projects with billable
    /// LaborCodes (excludes Admin=70 and NonBillable=80, plus overhead WBS1
```

### Line 142
```csharp
    /// </summary>
    private static double LoadTrailing12MoDirectLaborCost(
        OdbcConnection cn, DateTime since, double usdToCadRate, CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(t.RegAmt,0) + COALESCE(t.OvtAmt,0) + COALESCE(t.SpecialOvtAmt,0)) AS LaborCost
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.tkDetail t
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.EMCompany ec
       ON ec.Employee = t.Employee
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1
      AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE t.TransDate >= ?
  AND t.LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
  AND t.WBS1 NOT LIKE '[A-Z]%'
  AND t.WBS1 NOT LIKE '9[A-Z]%'
  AND t.WBS1 NOT LIKE '99%'
  AND t.Employee IS NOT NULL
  AND LTRIM(RTRIM(t.Employee)) <> ''
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = since });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var cadTotal = 0.0;
        var usaTotal = 0.0;
        while (r.Read())
        {
            var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
            if (string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase))
```

## Kor.Operations.App\Financials\Loaders\RevenueLoader.cs

### Line 178
```csharp
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Account codes at KOR are stored with a ".00" suffix (e.g. '4001.00').
            // Match on 4-char prefix so the predicate is robust to catalog-level
            // formatting choices (some installations store as '4001', some padded).
            cmd.CommandText = $@"
SELECT
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(-Amount) AS Invoiced
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.LedgerAR
WHERE TransType = 'IN'
  AND LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ('4001', '4003', '4210', '4220', '4240')
  AND Period >= ?
  AND WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = sincePeriodInt });
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                var amt = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
                if (string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase))
                    usaTotal += amt;
                else
                    cadTotal += amt;
            }
        }

        return cadTotal + (usaTotal * usdToCadRate);
    }

```

### Line 215
```csharp
    private static Dictionary<string, CalRow> TryLoadCalendar(OdbcConnection cn, CancellationToken ct)
    {
        var map = new Dictionary<string, CalRow>(StringComparer.OrdinalIgnoreCase);

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT Period, StartDate, EndDate
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.CFGAcctngCalendarData
ORDER BY Period;";

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();

            var period = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
            if (period.Length == 0) continue;

            var start = ExecutiveSummaryLoaderSupport.GetDate(r, 1);
            var end = ExecutiveSummaryLoaderSupport.GetDate(r, 2);
            if (start == DateTime.MinValue || end == DateTime.MinValue) continue;

            map[period] = new CalRow(period, start, end);
        }

        return map;
    }

    private static Dictionary<string, PrAgg> LoadPrSummaryByPeriod(OdbcConnection cn, List<string> wbs1, double usdToCadRate, CancellationToken ct)
    {
        var acc = new Dictionary<string, PrAgg>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
        {
            using var cmd = cn.CreateCommand();
```

### Line 250
```csharp
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Bucket by Org so USA-org per-period sums can be FX-converted before being added
            // to the period total (Revenue, Billed, AR, Unbilled split). Otherwise WIP-derived
            // metrics (BuiltSeries, Earned, Overbilled) inherit a CAD+USD mix.
            cmd.CommandText = $@"
SELECT sm.Period,
       CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
       SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue,0) END) AS Revenue,
       SUM(COALESCE(sm.Billed,0))   AS Billed,
       SUM(COALESCE(sm.AR,0))       AS AR,
       SUM(COALESCE(sm.Unbilled,0)) AS UnbilledNet,
       SUM(CASE WHEN COALESCE(sm.Unbilled,0) > 0 THEN COALESCE(sm.Unbilled,0) ELSE 0 END) AS UnbilledEarned,
       SUM(CASE WHEN COALESCE(sm.Unbilled,0) < 0 THEN -COALESCE(sm.Unbilled,0) ELSE 0 END) AS Overbilled
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY sm.Period, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();

                var period = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (period.Length == 0) continue;

                var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;

                var rev = ExecutiveSummaryLoaderSupport.GetDouble(r, 2) * fx;
                var billed = ExecutiveSummaryLoaderSupport.GetDouble(r, 3) * fx;
                var ar = ExecutiveSummaryLoaderSupport.GetDouble(r, 4) * fx;
```

### Line 330
```csharp
        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;

            var periodPlaceholders = ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(periods.Count);
            cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM({field}) AS Amount
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
  AND sm.Period IN ({periodPlaceholders})
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
            foreach (var period in periods)
            {
                cmd.Parameters.Add(new OdbcParameter
                {
                    OdbcType = OdbcType.VarChar,
                    Value = period
                });
            }

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (w.Length == 0) continue;
                var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
```

### Line 395
```csharp
            return map;

        foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    WBS1,
    COALESCE(NULLIF(LTRIM(RTRIM(Name)),''), WBS1) AS PayerName
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR
WHERE WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
  AND (WBS2 IS NULL OR LTRIM(RTRIM(WBS2)) = '');";
            ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (w.Length == 0) continue;
                var payer = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                map[w] = payer.Length == 0 ? w : payer;
            }
        }

        return map;
    }

    private static BuiltSeries BuildSeries(Dictionary<string, PrAgg> prByPeriod, Dictionary<string, CalRow> cal, int points)
    {
        var unbilledColumnHasAny =
            prByPeriod.Values.Any(p =>
                Math.Abs(p.UnbilledNet) > 1e-9 ||
                Math.Abs(p.UnbilledEarned) > 1e-9 ||
                Math.Abs(p.Overbilled) > 1e-9);
```

## Kor.Operations.App\Financials\Loaders\UtilizationLoader.cs

### Line 61
```csharp
    private static UtilAgg LoadUtilization30Firmwide(OdbcConnection cn, CancellationToken ct)
    {
        var start = DateTime.Today.AddDays(-30);

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    SUM(COALESCE(t.RegHrs,0) + COALESCE(t.OvtHrs,0) + COALESCE(t.SpecialOvtHrs,0)) AS TotalHours,
    SUM(CASE WHEN t.LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0) + COALESCE(t.OvtHrs,0) + COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHours
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.tkDetail t
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.EMCompany ec
       ON ec.Employee = t.Employee
WHERE t.TransDate >= ?
  AND t.Employee IS NOT NULL
  AND LTRIM(RTRIM(t.Employee)) <> ''
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R';";

        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = start });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            var total = ExecutiveSummaryLoaderSupport.GetDouble(r, 0);
            var billable = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
            return new UtilAgg(billable, total);
        }
        return new UtilAgg(0, 0);
    }

    private static List<UtilizationProjectRow> LoadUtilization30ProjectRows(OdbcConnection cn, List<string>? wbs1, CancellationToken ct)
```

### Line 112
```csharp
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Per-project rows must use the SAME billable definition as the firmwide
            // headline (LaborCode-based with overhead WBS exclusions) so summing the
            // drilldown reproduces the headline ratio. Passing null/empty WBS1 runs
            // firmwide with no WBS1 IN clause, matching the headline.
            cmd.CommandText = $@"
SELECT
    t.WBS1,
    SUM(COALESCE(t.RegHrs,0) + COALESCE(t.OvtHrs,0) + COALESCE(t.SpecialOvtHrs,0)) AS TotalHours,
    SUM(CASE WHEN t.LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0) + COALESCE(t.OvtHrs,0) + COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHours
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.tkDetail t
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.EMCompany ec
       ON ec.Employee = t.Employee
WHERE t.TransDate >= ?
{inWbs1}  AND t.WBS1 IS NOT NULL
  AND LTRIM(RTRIM(t.WBS1)) <> ''
  AND t.Employee IS NOT NULL
  AND LTRIM(RTRIM(t.Employee)) <> ''
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.WBS1;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = start });
            if (chunk != null) ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (w.Length == 0) continue;
                var total = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
```

## Kor.Operations.App\Financials\Loaders\WipLoader.cs

### Line 129
```csharp

        if (recentPeriods.Count == 0)
            return false;

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        cmd.CommandText = $@"
SELECT
    SUM(COALESCE(Revenue, 0)) AS RawRevenue,
    SUM(COALESCE(Billed, 0)) AS Billed
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain
WHERE Period IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(recentPeriods.Count)});";
        foreach (var period in recentPeriods)
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return false;

        var recentRevenue = ExecutiveSummaryLoaderSupport.GetDouble(r, 0);
        var recentBilled = ExecutiveSummaryLoaderSupport.GetDouble(r, 1);
        // Math.Abs handles the credit-side storage convention (Revenue stored
        // as -Amount). A signed comparison reads negative values as "no
        // revenue" and incorrectly suppresses the WIP card.
        return recentBilled > 0.0 && Math.Abs(recentRevenue) > 0.01 * recentBilled;
    }

    private static string? LoadMaxPrSummaryPeriod(OdbcConnection cn, CancellationToken ct)
    {
        try
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $"SELECT MAX(Period) FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var v = cmd.ExecuteScalar();
```

### Line 157
```csharp
    private static string? LoadMaxPrSummaryPeriod(OdbcConnection cn, CancellationToken ct)
    {
        try
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $"SELECT MAX(Period) FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var v = cmd.ExecuteScalar();
            if (v == null || v == DBNull.Value) return null;
            var p = (Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            return (p.Length == 6 && p.All(char.IsDigit)) ? p : null;
        }
        catch
        {
            return null;
        }
    }

    private static (double Earned, double Overbilled, double Net) LoadFirmwideWipProxyBalance(OdbcConnection cn, string asOfPeriod, double usdToCadRate, CancellationToken ct)
    {
        var period = (asOfPeriod ?? string.Empty).Trim();
        if (period.Length != 6 || !period.All(char.IsDigit))
        {
            // Don't fall back to a calendar month — that anchors WIP to a period Deltek
            // may not have posted yet (or to a future month). If MAX(Period) is unavailable,
            // return zeros so the tile shows "no data" rather than fabricated numbers.
            period = LoadMaxPrSummaryPeriod(cn, ct) ?? string.Empty;
            if (period.Length != 6 || !period.All(char.IsDigit))
                return (0.0, 0.0, 0.0);
        }

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = SqlTimeouts.Batch;
        // Per-project Net (source currency), bucketed by master-project Org. Earned vs
        // Overbilled split happens after FX conversion so a USA project's sign in CAD
        // matches its sign in USD (FX is positive multiplier). Net is computed as
```

### Line 191
```csharp
        // Per-project Net (source currency), bucketed by master-project Org. Earned vs
        // Overbilled split happens after FX conversion so a USA project's sign in CAD
        // matches its sign in USD (FX is positive multiplier). Net is computed as
        // Billed - Revenue (sign-flipped from raw storage) so positive=earned-not-billed
        // and negative=overbilled — matches Deltek's credit-side sign convention on
        // PRSummaryMain.Revenue.
        cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(sm.Billed,0) - COALESCE(sm.Revenue,0)) AS Net
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period <= ?
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";

        cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });

        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        double earned = 0.0, overbilled = 0.0, net = 0.0;
        while (r.Read())
        {
            var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
            var rawNet = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
            var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
            var nNet = rawNet * fx;
            if (nNet > 0) earned += nNet;
            else if (nNet < 0) overbilled += -nNet;
            net += nNet;
        }
        return (earned, overbilled, net);
    }

    private static List<WipProjectBreakdownRow> LoadWipProjectBreakdownByProject(
        OdbcConnection cn,
```

### Line 250
```csharp
            // to CAD-equivalent before being split into earned/overbilled. PRSummaryMain
            // stores Revenue and Unbilled with Deltek's credit-side sign convention
            // (negative = recognized revenue), so both branches flip the sign here so
            // downstream code reads positive=earned and negative=overbilled cleanly.
            if (useUnbilledAsOf)
            {
                cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(-COALESCE(sm.Unbilled,0)) AS Net
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period <= ?
  AND sm.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
            }
            else
            {
                cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(sm.Billed,0) - COALESCE(sm.Revenue,0)) AS Net
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period <= ?
  AND sm.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
            }

```

### Line 266
```csharp
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
            }
            else
            {
                cmd.CommandText = $@"
SELECT
    sm.WBS1,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
    SUM(COALESCE(sm.Billed,0) - COALESCE(sm.Revenue,0)) AS Net
FROM [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PRSummaryMain sm
LEFT JOIN [{ExecutiveSummaryLoaderSupport.Catalog}].dbo.PR pr
  ON pr.WBS1 = sm.WBS1 AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE sm.Period <= ?
  AND sm.WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY sm.WBS1, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = period });
                ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);
            }

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var w = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                if (w.Length == 0) continue;
                var bucket = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                var rawNet = ExecutiveSummaryLoaderSupport.GetDouble(r, 2);
                var fx = string.Equals(bucket, "USA", StringComparison.OrdinalIgnoreCase) ? usdToCadRate : 1.0;
                var net = rawNet * fx;
                var earned = Math.Max(net, 0.0);
                var over = Math.Max(-net, 0.0);

                rows[w] = new WipProjectBreakdownRow(w, earned, over, net, period);
            }
        }
```

## Kor.Operations.App\Financials\ProjectFinancialDetailWindow.xaml.cs

### Line 73
```csharp
                await Task.Run(() => cn.Open());

                // Query 1: All PR elements at WBS2+WBS3 granularity
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandTimeout = 30;
                    cmd.CommandText = $@"
SELECT COALESCE(WBS2,''), COALESCE(WBS3,''), COALESCE(ChargeType,''), COALESCE(RevenueMethod,''), Fee, Name
FROM [{catalog}].dbo.PR
WHERE WBS1 = ?
  AND WBS2 IS NOT NULL AND LTRIM(RTRIM(WBS2)) <> ''
ORDER BY WBS2, WBS3";
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                    using var r = await Task.Run(() => cmd.ExecuteReader());
                    while (r.Read())
                    {
                        var wbs2 = r.GetString(0).Trim();
                        var wbs3 = r.GetString(1).Trim();
                        var ct = r.GetString(2).Trim();
                        var rm = r.GetString(3).Trim();
                        var fee = Convert.ToDouble(r.GetValue(4));
                        var name = r.GetString(5).Trim();
                        prRows.Add((wbs2, wbs3, ct, rm, fee, name));
                    }
                }

                // Query 2: Parent row
                using (var cmd2 = cn.CreateCommand())
                {
                    cmd2.CommandTimeout = 15;
                    cmd2.CommandText = $@"
SELECT Fee, COALESCE(ChargeType,''), COALESCE(RevenueMethod,''), Name
FROM [{catalog}].dbo.PR
WHERE WBS1 = ? AND (WBS2 IS NULL OR LTRIM(RTRIM(WBS2)) = '')";
                    cmd2.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                    using var r2 = await Task.Run(() => cmd2.ExecuteReader());
                    if (r2.Read())
```

### Line 97
```csharp
                }

                // Query 2: Parent row
                using (var cmd2 = cn.CreateCommand())
                {
                    cmd2.CommandTimeout = 15;
                    cmd2.CommandText = $@"
SELECT Fee, COALESCE(ChargeType,''), COALESCE(RevenueMethod,''), Name
FROM [{catalog}].dbo.PR
WHERE WBS1 = ? AND (WBS2 IS NULL OR LTRIM(RTRIM(WBS2)) = '')";
                    cmd2.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                    using var r2 = await Task.Run(() => cmd2.ExecuteReader());
                    if (r2.Read())
                    {
                        parentFee = Convert.ToDouble(r2.GetValue(0));
                    }
                }

                // Query 3: PRSummaryMain revenue at WBS2+WBS3 granularity
                using (var cmd3 = cn.CreateCommand())
                {
                    cmd3.CommandTimeout = 30;
                    cmd3.CommandText = $@"
SELECT COALESCE(WBS2,''), COALESCE(WBS3,''),
       SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue,0) END) AS EffectiveRevenue,
       SUM(COALESCE(BilledTaxes,0))
FROM [{catalog}].dbo.PRSummaryMain
WHERE WBS1 = ?
GROUP BY WBS2, WBS3
ORDER BY WBS2, WBS3";
                    cmd3.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                    using var r3 = await Task.Run(() => cmd3.ExecuteReader());
                    while (r3.Read())
                    {
                        var wbs2 = r3.GetString(0).Trim();
                        var wbs3 = r3.GetString(1).Trim();
                        var rev = Convert.ToDouble(r3.GetValue(2));
```

### Line 113
```csharp
                }

                // Query 3: PRSummaryMain revenue at WBS2+WBS3 granularity
                using (var cmd3 = cn.CreateCommand())
                {
                    cmd3.CommandTimeout = 30;
                    cmd3.CommandText = $@"
SELECT COALESCE(WBS2,''), COALESCE(WBS3,''),
       SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue,0) END) AS EffectiveRevenue,
       SUM(COALESCE(BilledTaxes,0))
FROM [{catalog}].dbo.PRSummaryMain
WHERE WBS1 = ?
GROUP BY WBS2, WBS3
ORDER BY WBS2, WBS3";
                    cmd3.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                    using var r3 = await Task.Run(() => cmd3.ExecuteReader());
                    while (r3.Read())
                    {
                        var wbs2 = r3.GetString(0).Trim();
                        var wbs3 = r3.GetString(1).Trim();
                        var rev = Convert.ToDouble(r3.GetValue(2));
                        revLookup[(wbs2, wbs3)] = rev;
                    }
                }

                // Query 4: tkDetail hours at WBS2+WBS3 granularity
                using (var cmd4 = cn.CreateCommand())
                {
                    cmd4.CommandTimeout = 30;
                    cmd4.CommandText = $@"
SELECT COALESCE(WBS2,''), COALESCE(WBS3,''),
       SUM(COALESCE(RegHrs,0)), SUM(COALESCE(OvtHrs,0)), SUM(COALESCE(SpecialOvtHrs,0))
FROM [{catalog}].dbo.tkDetail
WHERE WBS1 = ?
  AND COALESCE(LineItemApprovalStatus,'') <> 'R'
GROUP BY WBS2, WBS3
ORDER BY WBS2, WBS3";
```

### Line 136
```csharp
                }

                // Query 4: tkDetail hours at WBS2+WBS3 granularity
                using (var cmd4 = cn.CreateCommand())
                {
                    cmd4.CommandTimeout = 30;
                    cmd4.CommandText = $@"
SELECT COALESCE(WBS2,''), COALESCE(WBS3,''),
       SUM(COALESCE(RegHrs,0)), SUM(COALESCE(OvtHrs,0)), SUM(COALESCE(SpecialOvtHrs,0))
FROM [{catalog}].dbo.tkDetail
WHERE WBS1 = ?
  AND COALESCE(LineItemApprovalStatus,'') <> 'R'
GROUP BY WBS2, WBS3
ORDER BY WBS2, WBS3";
                    cmd4.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                    using var r4 = await Task.Run(() => cmd4.ExecuteReader());
                    while (r4.Read())
                    {
                        var wbs2 = r4.GetString(0).Trim();
                        var wbs3 = r4.GetString(1).Trim();
                        var reg = Convert.ToDouble(r4.GetValue(2));
                        var ovt = Convert.ToDouble(r4.GetValue(3));
                        var specialOvt = Convert.ToDouble(r4.GetValue(4));
                        var total = reg + ovt + specialOvt;
                        hrsLookup[(wbs2, wbs3)] = total;
                    }
                }

                // Classify elements into breakdown categories
                var initialPhases = new ObservableCollection<FeeBreakdownRow>();
                var fixedExtras = new ObservableCollection<FeeBreakdownRow>();
                var absorbedExtras = new ObservableCollection<FeeBreakdownRow>();
                var hourlyExtras = new ObservableCollection<FeeBreakdownRow>();

                foreach (var row in prRows)
                {
                    bool isExtra = row.Wbs2.StartsWith("X", StringComparison.OrdinalIgnoreCase);
```

### Line 354
```csharp

                using var cn = factory.Create();
                await Task.Run(() => cn.Open());

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 30;
                cmd.CommandText = $@"
SELECT tk.Employee,
       COALESCE(em.FirstName,'') + ' ' + COALESCE(em.LastName,'') AS EmployeeName,
       tk.Category,
       SUM(COALESCE(tk.RegHrs,0) + COALESCE(tk.OvtHrs,0) + COALESCE(tk.SpecialOvtHrs,0)) AS TotalHrs,
       MIN(tk.TransDate) AS FirstEntry,
       MAX(tk.TransDate) AS LastEntry
FROM [{catalog}].dbo.tkDetail tk
LEFT JOIN [{catalog}].dbo.EMMain em ON em.Employee = tk.Employee
WHERE tk.WBS1 = ? AND tk.WBS2 = ? AND tk.WBS3 = ?
  AND COALESCE(tk.LineItemApprovalStatus,'') <> 'R'
GROUP BY tk.Employee, COALESCE(em.FirstName,'') + ' ' + COALESCE(em.LastName,''), tk.Category
HAVING SUM(COALESCE(tk.RegHrs,0) + COALESCE(tk.OvtHrs,0) + COALESCE(tk.SpecialOvtHrs,0)) > 0
ORDER BY SUM(COALESCE(tk.RegHrs,0) + COALESCE(tk.OvtHrs,0) + COALESCE(tk.SpecialOvtHrs,0)) DESC";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = row.Wbs2 });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = row.Wbs3 });

                using var r = await Task.Run(() => cmd.ExecuteReader());
                while (r.Read())
                {
                    var empCode = r.GetString(0).Trim();
                    var name = r.GetString(1).Trim();
                    var category = r.IsDBNull(2) ? "" : r.GetString(2).Trim();
                    var hrs = Convert.ToDouble(r.GetValue(3));
                    var firstDate = r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4);
                    var lastDate = r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5);
                    row.EmployeeBreakdown.Add(new EmployeeHoursRow
                    {
                        EmployeeCode = empCode,
                        Employee = name,
```

### Line 445
```csharp

                using var cn = factory.Create();
                await Task.Run(() => cn.Open());

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 30;
                cmd.CommandText = $@"
SELECT TransDate, Category, RegHrs, OvtHrs, SpecialOvtHrs, TransComment
FROM [{catalog}].dbo.tkDetail
WHERE WBS1 = ? AND WBS2 = ? AND WBS3 = ? AND Employee = ? AND Category = ?
  AND COALESCE(LineItemApprovalStatus,'') <> 'R'
ORDER BY TransDate";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = feeRow.Wbs2 });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = feeRow.Wbs3 });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = empRow.EmployeeCode });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = empRow.Category });

                var rawEntries = new System.Collections.Generic.List<(DateTime Date, double Reg, double Ovt, double SpecialOvt, string? Comment)>();
                using var r = await Task.Run(() => cmd.ExecuteReader());
                while (r.Read())
                {
                    var date = r.GetDateTime(0);
                    var reg = Convert.ToDouble(r.GetValue(2));
                    var ovt = Convert.ToDouble(r.GetValue(3));
                    var specialOvt = Convert.ToDouble(r.GetValue(4));
                    var comment = r.IsDBNull(5) ? null : r.GetString(5).Trim();
                    rawEntries.Add((date, reg, ovt, specialOvt, string.IsNullOrWhiteSpace(comment) ? null : comment));
                }

                // Group by month
                var grouped = new System.Collections.Generic.SortedDictionary<(int Year, int Month), System.Collections.Generic.List<(DateTime Date, double Reg, double Ovt, double SpecialOvt, string? Comment)>>();
                foreach (var entry in rawEntries)
                {
                    var key = (entry.Date.Year, entry.Date.Month);
                    if (!grouped.ContainsKey(key))
                        grouped[key] = new();
```

### Line 541
```csharp
                await cn.OpenAsync().ConfigureAwait(true);
#else
                cn.Open();
#endif
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                cmd.CommandText = $@"
SELECT 
    e.FirstName + ' ' + e.LastName AS EmployeeName,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHours
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMMain e ON t.Employee = e.Employee
WHERE t.WBS1 = ?
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY e.FirstName, e.LastName
ORDER BY TotalHours DESC";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = _wbs1 });

#if NET6_0_OR_GREATER
                using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(true);
#else
                using var r = cmd.ExecuteReader();
#endif
                _teamRows.Clear();
                while (r.Read())
                {
                    var name = r.IsDBNull(0) ? "" : (Convert.ToString(r.GetValue(0)) ?? "");
                    name = name.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        name = "(Unknown)";

                    double hrs = 0.0;
                    if (!r.IsDBNull(1))
                    {
                        var v = r.GetValue(1);
                        if (v is double d) hrs = d;
                        else if (v is float f) hrs = f;
```

## Kor.Operations.App\PMTools\CalendarHeatmapPanel.xaml.cs

### Line 53
```csharp

                using var cn = factory.Create();
                cn.Open();

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                cmd.CommandText = $@"
SELECT t.TransDate, t.WBS1, pr.Name,
       SUM(COALESCE(t.RegHrs,0)) + SUM(COALESCE(t.OvtHrs,0)) + SUM(COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1
      AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE t.Employee = ?
  AND t.TransDate >= ?
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.TransDate, t.WBS1, pr.Name
ORDER BY t.TransDate";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = _staff.EmployeeId });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = startDate });

                var result = new Dictionary<DateTime, List<DayProjectEntry>>();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (r.IsDBNull(0)) continue;
                    var raw = r.GetValue(0);
                    DateTime date;
                    if (raw is DateTime dt) date = dt.Date;
                    else if (DateTime.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var p)) date = p.Date;
                    else continue;

                    var wbs1 = GetTrimmed(r, 1);
                    var name = GetTrimmed(r, 2);
                    var totalHours = GetDouble(r, 3);
                    if (totalHours <= 0) continue;
```

## Kor.Operations.App\PMTools\HistoricalAnalyticsService.cs

### Line 100
```csharp
            // 30  DmFirstName  31  DmLastName  32  CustDraftingManager(id)
            // 33  TotalInspections  34  LastMonthInspections  35  ClientID  36  HourlyRevenue  37  pr.Org
            var inspMonthEnd = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var inspMonthStart = inspMonthEnd.AddMonths(-1);
            var inspMonthStartStr = inspMonthStart.ToString("yyyy-MM-dd");
            var inspMonthEndStr = inspMonthEnd.ToString("yyyy-MM-dd");
            cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.Name,
    em.FirstName,
    em.LastName,
    pr.ProjMgr,
    pctf.CustProjectPhase,
    pr.Status,
    pr.OpenDate,
    pr.CloseDate,
    pr.Fee,
    ISNULL(billed.FeeBilled, 0)   AS FeeBilled,
    ISNULL(labor.EngHrs, 0)       AS EngHrs,
    ISNULL(labor.DraftHrs, 0)     AS DraftHrs,
    ISNULL(labor.InspHrs, 0)      AS InspHrs,
    ISNULL(labor.DocPrepHrs, 0)   AS DocPrepHrs,
    ISNULL(labor.GenHrs, 0)       AS GenHrs,
    ISNULL(labor.AdminHrs, 0)     AS AdminHrs,
    ISNULL(labor.NonBillHrs, 0)   AS NonBillHrs,
    ISNULL(labor.TotalAllHrs, 0)  AS TotalAllHrs,
    ISNULL(labor.BillableHrs, 0)  AS BillableHrs,
    ISNULL(sub.SubCost, 0)        AS SubCost,
    ISNULL(ar.ArTotal, 0)         AS ArTotal,
    ISNULL(ar.ArCurrent, 0)       AS ArCurrent,
    ISNULL(ar.Ar31To60, 0)        AS Ar31To60,
    ISNULL(ar.Ar61To90, 0)        AS Ar61To90,
    ISNULL(ar.Ar90Plus, 0)        AS Ar90Plus,
    ISNULL(unposted.UnpostedFeeBilled, 0) AS UnpostedFeeBilled,
    pctf.CustConstructionType,
    pctf.CustProjectCategory,
```

### Line 356
```csharp
            var result = new Dictionary<string, List<PeriodRevenue>>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            cn.Open();

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT WBS1, Period,
       SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue, 0) END) AS Revenue,
       SUM(COALESCE(Billed, 0)) AS Billed
FROM [{catalog}].dbo.PRSummaryMain
GROUP BY WBS1, Period
ORDER BY WBS1, Period;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(wbs1)) continue;
                var period = GetTrimmed(r, 1);
                var revenue = GetDouble(r, 2);
                var billed = GetDouble(r, 3);
                if (revenue == 0 && billed == 0) continue;

                if (!result.TryGetValue(wbs1, out var list))
                    result[wbs1] = list = new List<PeriodRevenue>();
                list.Add(new PeriodRevenue(period, revenue, billed));
            }
            return result;
        }

        private FirmUtilizationStats LoadFirmUtilizationSync(CancellationToken ct)
        {
            var dsn     = string.IsNullOrWhiteSpace(_opts.Dsn) ? "Deltek" : _opts.Dsn;
            var catalog = DeltekCatalogValidator.ResolveCatalog(_opts.Catalog);
```

### Line 396
```csharp
            using var cn = factory.Create();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // Query ALL tkDetail hours firm-wide — no WBS1 filter.
            // Billable = LaborCode NOT IN (Admin, NonBillable), matching Staff Utilization definition.
            cmd.CommandText = $@"
SELECT
    YEAR(TransDate) AS Yr,
    SUM(COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0)) AS TotalHrs,
    SUM(CASE WHEN LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
              AND WBS1 NOT LIKE '[A-Z]%'
              AND WBS1 NOT LIKE '9[A-Z]%'
              AND WBS1 NOT LIKE '99%'
             THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs
FROM [{catalog}].dbo.tkDetail
WHERE TransDate IS NOT NULL
  AND COALESCE(LineItemApprovalStatus,'') <> 'R'
GROUP BY YEAR(TransDate)
ORDER BY YEAR(TransDate);";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var byYear = new Dictionary<int, (double Total, double Billable)>();
            var totalAll = 0.0;
            var billableAll = 0.0;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var yr = (int)GetDouble(r, 0);
                var total = GetDouble(r, 1);
                var billable = GetDouble(r, 2);
                if (yr > 0)
                    byYear[yr] = (total, billable);
                totalAll += total;
                billableAll += billable;
            }
```

### Line 448
```csharp

            var result = new List<EmployeeProjectHours>();
            using var cn = factory.Create();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    t.Employee,
    e.FirstName,
    e.LastName,
    t.WBS1,
    SUM(CASE WHEN t.LaborCode IN ({LaborCodes.Engineering}, {LaborCodes.Checking}) THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS EngHrs,
    SUM(CASE WHEN t.LaborCode = {LaborCodes.Drafting} THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS DraftHrs,
    SUM(CASE WHEN t.LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs,
    MIN(ec.HireDate) AS HireDate
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMMain e ON e.Employee = t.Employee
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
WHERE t.Employee IS NOT NULL
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.Employee, e.FirstName, e.LastName, t.WBS1;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var empId = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(empId)) continue;
                var name = $"{GetTrimmed(r, 1)} {GetTrimmed(r, 2)}".Trim();
```

### Line 507
```csharp

            var result = new List<EmployeeWeeklyHours>();
            using var cn = factory.Create();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    t.Employee,
    e.FirstName,
    e.LastName,
    DATEADD(day, -DATEDIFF(day, '18991231', CAST(t.TransDate AS date)) % 7, CAST(t.TransDate AS date)) AS WeekStart,
    SUM(CASE WHEN t.LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMMain e ON e.Employee = t.Employee
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
WHERE t.Employee IS NOT NULL
  AND t.TransDate IS NOT NULL
  AND t.TransDate >= DATEADD(week, -12, CAST(GETDATE() AS date))
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.Employee, e.FirstName, e.LastName,
         DATEADD(day, -DATEDIFF(day, '18991231', CAST(t.TransDate AS date)) % 7, CAST(t.TransDate AS date))
ORDER BY e.LastName, e.FirstName, WeekStart;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var empId = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(empId)) continue;
```

### Line 571
```csharp

            var result = new List<EmployeeRate>();
            using var cn = factory.Create();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    e.Employee,
    e.FirstName,
    e.LastName,
    COALESCE(ec.ProvBillRate, 0) AS BillingRate,
    COALESCE(ec.ProvCostRate, 0) AS CostRate
FROM [{catalog}].dbo.EMMain e
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = e.Employee
WHERE UPPER(COALESCE(ec.Status, 'A')) = 'A'
ORDER BY e.LastName, e.FirstName";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var empId = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(empId)) continue;
                var name = $"{GetTrimmed(r, 1)} {GetTrimmed(r, 2)}".Trim();
                if (string.IsNullOrWhiteSpace(name)) name = empId;

                var billing = GetDouble(r, 3);
                var rawCost = GetDouble(r, 4);
                var isPartner = empId.StartsWith("P", StringComparison.OrdinalIgnoreCase);
                var effectiveCost = isPartner ? _opts.PartnerImputedCostRate : rawCost;

                result.Add(new EmployeeRate
                {
                    EmployeeId = empId,
                    EmployeeName = name,
```

### Line 624
```csharp

            var result = new List<QuarterlyEmployeeHours>();
            using var cn = factory.Create();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    t.Employee,
    e.FirstName,
    e.LastName,
    t.WBS1,
    DATEPART(YEAR, t.TransDate)    AS Yr,
    DATEPART(QUARTER, t.TransDate) AS Qtr,
    SUM(CASE WHEN t.LaborCode IN ({LaborCodes.Engineering}, {LaborCodes.Checking}) THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS EngHrs,
    SUM(CASE WHEN t.LaborCode = {LaborCodes.Drafting} THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS DraftHrs,
    SUM(CASE WHEN t.LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS TotalHrs
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMMain e ON e.Employee = t.Employee
WHERE t.Employee IS NOT NULL
  AND t.TransDate >= '2020-01-01'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.Employee, e.FirstName, e.LastName, t.WBS1,
         DATEPART(YEAR, t.TransDate), DATEPART(QUARTER, t.TransDate);";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var empId = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(empId)) continue;
```

## Kor.Operations.App\PMTools\ProjectWeekDrillDownWindow.xaml.cs

### Line 64
```csharp

        private List<WeekDetailRow> LoadWeekRowsForProject(OdbcConnection cn, DateTime startDate)
        {
            var catalog = DeltekCatalogValidator.ResolveCatalog(_odbcOptions.Catalog);
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            cmd.CommandText = $@"
SELECT
    t.TransDate,
    SUM(COALESCE(t.RegHrs,0)) AS RegHrs,
    SUM(COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS OvtHrs
FROM [{catalog}].dbo.tkDetail t
WHERE t.Employee = ?
  AND t.TransDate >= ?
  AND t.WBS1 = ?
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.TransDate
ORDER BY t.TransDate";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = _staff.EmployeeId });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = startDate });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = _project.Wbs1 });

            var byWeek = new SortedDictionary<DateTime, (double Reg, double Ovt)>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.IsDBNull(0)) continue;
                var raw = r.GetValue(0);
                DateTime date;
                if (raw is DateTime dt) date = dt.Date;
                else if (DateTime.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var p)) date = p.Date;
                else continue;

                var dow = (int)date.DayOfWeek;
                var monday = date.AddDays(dow == 0 ? -6 : 1 - dow).Date;

```

## Kor.Operations.App\PMTools\StaffHoursDetailWindow.xaml.cs

### Line 87
```csharp

        private List<ProjectDetailRow> LoadProjectRows(OdbcConnection cn, DateTime startDate)
        {
            var catalog = DeltekCatalogValidator.ResolveCatalog(_odbcOptions.Catalog);
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            cmd.CommandText = $@"
SELECT
    t.WBS1,
    pr.Name,
    pctf.CustProjectPhase,
    SUM(COALESCE(t.RegHrs,0)) AS RegHrs,
    SUM(COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS OvtHrs
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1
      AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
LEFT JOIN (
    SELECT WBS1, MAX(CustProjectPhase) AS CustProjectPhase
    FROM [{catalog}].dbo.ProjectCustomTabFields
    GROUP BY WBS1
) pctf ON pctf.WBS1 = t.WBS1
WHERE t.Employee = ?
  AND t.TransDate >= ?
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.WBS1, pr.Name, pctf.CustProjectPhase
ORDER BY (SUM(COALESCE(t.RegHrs,0)) + SUM(COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0))) DESC";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar,  Value = _staff.EmployeeId });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = startDate });

            var rows       = new List<ProjectDetailRow>();
            var grandTotal = _staff.TwelveWkHrs > 0 ? _staff.TwelveWkHrs : 1.0;

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
```

### Line 147
```csharp

        private List<WeekDetailRow> LoadWeekRows(OdbcConnection cn, DateTime startDate)
        {
            var catalog = DeltekCatalogValidator.ResolveCatalog(_odbcOptions.Catalog);
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            cmd.CommandText = $@"
SELECT
    t.TransDate,
    SUM(COALESCE(t.RegHrs,0)) AS RegHrs,
    SUM(COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0)) AS OvtHrs
FROM [{catalog}].dbo.tkDetail t
WHERE t.Employee = ?
  AND t.TransDate >= ?
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.TransDate
ORDER BY t.TransDate";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar,  Value = _staff.EmployeeId });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = startDate });

            // Accumulate daily hours into Mon-anchored week buckets
            var byWeek = new SortedDictionary<DateTime, (double Reg, double Ovt)>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.IsDBNull(0)) continue;
                var raw = r.GetValue(0);
                DateTime date;
                if (raw is DateTime dt) date = dt.Date;
                else if (DateTime.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var p)) date = p.Date;
                else continue;

                // Snap to the Monday of this day's week
                var dow    = (int)date.DayOfWeek; // 0=Sun … 6=Sat
                var monday = date.AddDays(dow == 0 ? -6 : 1 - dow).Date;

```

## Kor.Operations.App\PMTools\StaffUtilizationWindow.xaml.cs

### Line 91
```csharp
                // Cost columns are FX'd to CAD-equivalent: tkDetail.RegAmt is
                // denominated in the project's currency (verified 2026-05-06
                // against KOR's catalog), so we LEFT JOIN PR for the master
                // row's Org and CASE-FX USA-org rows. Hours columns are
                // currency-agnostic and stay raw.
                var fxRate = _usdToCadRate;
                cmd.CommandText = $@"
SELECT
    t.Employee,
    e.FirstName + ' ' + e.LastName AS EmployeeName,
    SUM(CASE WHEN t.TransDate >= ? THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS WeekHrs,
    SUM(CASE WHEN t.TransDate >= ? THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS FourWkHrs,
    SUM(COALESCE(t.RegHrs,0))                                     AS TwelveWkRegHrs,
    SUM(COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0))         AS TwelveWkOvtHrs,
    SUM(CASE WHEN t.LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    COUNT(DISTINCT t.WBS1) AS ProjectCount,
    SUM(
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA'
             THEN (COALESCE(t.RegAmt,0)+COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)) * ?
             ELSE  COALESCE(t.RegAmt,0)+COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)
        END
    ) AS TwelveWkLaborCost,
    SUM(
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA'
             THEN (COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)) * ?
             ELSE  COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)
        END
    ) AS TwelveWkOvertimeCost
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMMain e ON t.Employee = e.Employee
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
LEFT JOIN [{catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1
```

## Kor.Operations.App\Services\DeltekHeadshotProvider.cs

### Line 37
```csharp
            return await Task.Run(() =>
            {
                using var conn = new OdbcConnection(ConnStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                cmd.CommandText = @"SELECT TOP 1 FirstName, LastName FROM EMMain WHERE EMail = ?";
                cmd.Parameters.Add("email", OdbcType.NVarChar).Value = email;

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                var first = r.IsDBNull(0) ? "" : r.GetString(0).Trim();
                var last = r.IsDBNull(1) ? "" : r.GetString(1).Trim();
                var full = $"{first} {last}".Trim();
                return string.IsNullOrWhiteSpace(full) ? null : full;
            });
        }

        // ---- PHOTO LOOKUP (EMPhoto -> fallback EMMain.EmployeePhoto) ----
        public async Task<BitmapImage?> TryGetByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;

            return await Task.Run(() =>
            {
                using var conn = new OdbcConnection(ConnStr);
                conn.Open();

                // Prefer EMPhoto.Photo
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandTimeout = SqlTimeouts.UiFacing;
                    cmd.CommandText = @"
                        SELECT TOP 1 p.Photo
                         FROM EMPhoto p
                         INNER JOIN EMMain m ON m.Employee = p.Employee
```

### Line 64
```csharp
                conn.Open();

                // Prefer EMPhoto.Photo
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandTimeout = SqlTimeouts.UiFacing;
                    cmd.CommandText = @"
                        SELECT TOP 1 p.Photo
                         FROM EMPhoto p
                         INNER JOIN EMMain m ON m.Employee = p.Employee
                         WHERE m.EMail = ?";
                    cmd.Parameters.Add("email", OdbcType.NVarChar).Value = email;

                    var bytes = ReadBlob(cmd);
                    var bmp = BytesToBitmap(bytes);
                    if (bmp != null) return bmp;
                }

                // Fallback to EMMain.EmployeePhoto
                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandTimeout = SqlTimeouts.UiFacing;
                    cmd2.CommandText = @"SELECT TOP 1 m.EmployeePhoto FROM EMMain m WHERE m.EMail = ?";
                    cmd2.Parameters.Add("email", OdbcType.NVarChar).Value = email;

                    var bytes = ReadBlob(cmd2);
                    return BytesToBitmap(bytes);
                }
            });
        }

        // Optional light check
        public async Task<bool> EmployeeHasPhotoAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            return await Task.Run(() =>
            {
```

### Line 80
```csharp
                }

                // Fallback to EMMain.EmployeePhoto
                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandTimeout = SqlTimeouts.UiFacing;
                    cmd2.CommandText = @"SELECT TOP 1 m.EmployeePhoto FROM EMMain m WHERE m.EMail = ?";
                    cmd2.Parameters.Add("email", OdbcType.NVarChar).Value = email;

                    var bytes = ReadBlob(cmd2);
                    return BytesToBitmap(bytes);
                }
            });
        }

        // Optional light check
        public async Task<bool> EmployeeHasPhotoAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            return await Task.Run(() =>
            {
                using var conn = new OdbcConnection(ConnStr);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                cmd.CommandText = @"SELECT TOP 1 1 FROM EMPhoto p INNER JOIN EMMain m ON m.Employee=p.Employee WHERE m.EMail = ?";
                cmd.Parameters.Add("email", OdbcType.NVarChar).Value = email;
                using var r1 = cmd.ExecuteReader();
                if (r1.Read()) return true;

                using var cmd2 = conn.CreateCommand();
                cmd2.CommandTimeout = SqlTimeouts.UiFacing;
                cmd2.CommandText = @"SELECT TOP 1 1 FROM EMMain WHERE EMail = ? AND EmployeePhoto IS NOT NULL";
                cmd2.Parameters.Add("email", OdbcType.NVarChar).Value = email;
                using var r2 = cmd2.ExecuteReader();
                return r2.Read();
```

### Line 100
```csharp
            {
                using var conn = new OdbcConnection(ConnStr);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                cmd.CommandText = @"SELECT TOP 1 1 FROM EMPhoto p INNER JOIN EMMain m ON m.Employee=p.Employee WHERE m.EMail = ?";
                cmd.Parameters.Add("email", OdbcType.NVarChar).Value = email;
                using var r1 = cmd.ExecuteReader();
                if (r1.Read()) return true;

                using var cmd2 = conn.CreateCommand();
                cmd2.CommandTimeout = SqlTimeouts.UiFacing;
                cmd2.CommandText = @"SELECT TOP 1 1 FROM EMMain WHERE EMail = ? AND EmployeePhoto IS NOT NULL";
                cmd2.Parameters.Add("email", OdbcType.NVarChar).Value = email;
                using var r2 = cmd2.ExecuteReader();
                return r2.Read();
            });
        }

        // ---- helpers ----
        private static byte[]? ReadBlob(OdbcCommand cmd)
        {
            var obj = cmd.ExecuteScalar();
            if (obj == null || obj is System.DBNull) return null;
            if (obj is byte[] arr) return arr;
            if (obj is System.Array a)
            {
                var bytes = new byte[a.Length];
                System.Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
                return bytes;
            }
            return null;
        }

        private static BitmapImage? BytesToBitmap(byte[]? bytes)
        {
```

### Line 107
```csharp
                cmd.Parameters.Add("email", OdbcType.NVarChar).Value = email;
                using var r1 = cmd.ExecuteReader();
                if (r1.Read()) return true;

                using var cmd2 = conn.CreateCommand();
                cmd2.CommandTimeout = SqlTimeouts.UiFacing;
                cmd2.CommandText = @"SELECT TOP 1 1 FROM EMMain WHERE EMail = ? AND EmployeePhoto IS NOT NULL";
                cmd2.Parameters.Add("email", OdbcType.NVarChar).Value = email;
                using var r2 = cmd2.ExecuteReader();
                return r2.Read();
            });
        }

        // ---- helpers ----
        private static byte[]? ReadBlob(OdbcCommand cmd)
        {
            var obj = cmd.ExecuteScalar();
            if (obj == null || obj is System.DBNull) return null;
            if (obj is byte[] arr) return arr;
            if (obj is System.Array a)
            {
                var bytes = new byte[a.Length];
                System.Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
                return bytes;
            }
            return null;
        }

        private static BitmapImage? BytesToBitmap(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;

            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
```

## Kor.Operations.App\Services\DeltekHealthProbe.cs

### Line 75
```csharp
                    });

                    using var cn = factory.Create();
                    cn.Open();

                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = "SELECT 1";
                    cmd.CommandTimeout = SqlTimeouts.UiFacing;
                    _ = cmd.ExecuteScalar();

                    return new Status
                    {
                        IsOnline = true,
                        Message = null,
                        CheckedUtc = checkedUtc
                    };
                }
                catch (Exception ex)
                {
                    // Keep message user-facing and short. Full exception details belong in logs.
                    Log.ForContext(typeof(DeltekHealthProbe))
                        .Warning(ex, "Deltek health probe failed. {ErrorType}: {ErrorMessage}", ex.GetType().Name, ex.Message);
                    string msg = "Deltek is unavailable (maintenance or network).";
                    return new Status
                    {
                        IsOnline = false,
                        Message = msg,
                        CheckedUtc = checkedUtc
                    };
                }
            });
        }
    }
}
```

## Kor.Operations.Business\BilledFinancialsService.cs

### Line 153
```csharp
            var prefixes = ExtractAccountPrefixes(accounts);
            if (prefixes.Count == 0)
                return new List<LedgerAmountRow>();

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT Account, Period, Org, SUM(-Amount) AS Amount
FROM [{_catalog}].dbo.LedgerAR
WHERE Period BETWEEN ? AND ?
  AND TransType = 'IN'
  AND LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ({MakePlaceholders(prefixes.Count)})
  AND (? IS NULL OR Org = ?)
GROUP BY Account, Period, Org;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = minPeriod });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = maxPeriod });
            foreach (var prefix in prefixes)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = prefix });
            AddNullableOrgParameters(cmd, orgFilter);

            return ReadAmountRows(cmd, ct);
        }

        private List<LedgerAmountRow> LoadLedgerRanges(
            OdbcConnection cn,
            int minPeriod,
            int maxPeriod,
            string? orgFilter,
            IReadOnlyList<AccountRange> ranges,
            CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            var rangeWhere = string.Join(
                "\n      OR ",
                ranges.Select(_ => "(RIGHT(REPLICATE('0', 13) + Account, 13) BETWEEN RIGHT(REPLICATE('0', 13) + ?, 13) AND RIGHT(REPLICATE('0', 13) + ?, 13))"));
```

### Line 185
```csharp
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            var rangeWhere = string.Join(
                "\n      OR ",
                ranges.Select(_ => "(RIGHT(REPLICATE('0', 13) + Account, 13) BETWEEN RIGHT(REPLICATE('0', 13) + ?, 13) AND RIGHT(REPLICATE('0', 13) + ?, 13))"));

            cmd.CommandText = $@"
SELECT Account, Period, Org, SUM(Amount) AS Amount
FROM (
    SELECT Account, Period, Org, Amount FROM [{_catalog}].dbo.LedgerAP   WHERE Period BETWEEN ? AND ?
    UNION ALL
    SELECT Account, Period, Org, Amount FROM [{_catalog}].dbo.LedgerEX   WHERE Period BETWEEN ? AND ?
    UNION ALL
    SELECT Account, Period, Org, Amount FROM [{_catalog}].dbo.LedgerMisc WHERE Period BETWEEN ? AND ?
) x
WHERE (
      {rangeWhere}
)
AND (? IS NULL OR Org = ?)
GROUP BY Account, Period, Org;";

            for (var i = 0; i < 3; i++)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = minPeriod });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = maxPeriod });
            }

            foreach (var range in ranges)
            {
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = range.StartAccount });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = range.EndAccount });
            }

            AddNullableOrgParameters(cmd, orgFilter);
            return ReadAmountRows(cmd, ct);
        }

```

### Line 235
```csharp
            var prefixes = ExtractAccountPrefixes(revenueAccounts);
            if (prefixes.Count == 0)
                return new BilledPostedReconciliation(billedRange, 0m, billedRange, maxPostedPeriod);

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT Account, Period, Org, SUM(-Amount) AS Amount
FROM [{_catalog}].dbo.GLSummary
WHERE Period BETWEEN ? AND ?
  AND LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ({MakePlaceholders(prefixes.Count)})
  AND (? IS NULL OR Org = ?)
GROUP BY Account, Period, Org;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = rangeStartPeriod });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = rangeEndPeriod });
            foreach (var prefix in prefixes)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = prefix });
            AddNullableOrgParameters(cmd, orgFilter);

            var posted = ReadAmountRows(cmd, ct).Sum(r => ApplyCurrency(r, convertUsaToCad, usdToCadRate));
            return new BilledPostedReconciliation(billedRange, posted, billedRange - posted, maxPostedPeriod);
        }

        private static IReadOnlyList<BilledLine> BuildLines(
            IReadOnlyList<int> periods,
            IReadOnlyList<LedgerAmountRow> revenueRows,
            IReadOnlyList<LedgerAmountRow> expenseRows,
            IReadOnlyList<LedgerAmountRow> otherIncomeRows,
            bool convertUsaToCad,
            decimal usdToCadRate,
            IReadOnlyDictionary<string, string> accountNames)
        {
            var lines = new List<BilledLine>();

            AddDetailLines(lines, "Revenue", 0, revenueRows);
            AddSectionTotal(lines, "Revenue", "Total Revenue", 0);
```

### Line 409
```csharp
    UNION ALL
    SELECT 'Misc' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate,
           l.Desc1, l.Desc2, l.Amount, l.Amount AS SignedAmount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{_catalog}].dbo.LedgerMisc l
    WHERE l.Period = ? AND l.Account = ? {whereOrg}";

                cmd.CommandText = $@"
SELECT
    l.Source,
    l.Period,
    MAX(l.TransDate) AS TransDate,
    COALESCE(NULLIF(l.Invoice, ''), NULLIF(l.Voucher, ''), NULLIF(l.RefNo, ''), '(none)') AS DocumentNo,
    COALESCE(
        NULLIF(cc.Name, ''),
        NULLIF(cv.Name, ''),
        NULLIF(LTRIM(RTRIM(COALESCE(em.FirstName, '') + ' ' + COALESCE(em.LastName, ''))), ''),
        NULLIF(l.Vendor, ''),
        NULLIF(l.Employee, ''),
        '(unmapped)') AS Counterparty,
    l.Account,
    l.TransType,
    MAX(COALESCE(NULLIF(l.Desc1, ''), NULLIF(l.Desc2, ''), '')) AS Description,
    SUM(l.SignedAmount) AS Amount,
    COUNT(*) AS EntryCount
FROM
({sourceSql}
) l
LEFT JOIN
(
    SELECT ar.Invoice, ar.WBS1, ar.ClientID,
           ROW_NUMBER() OVER (PARTITION BY ar.Invoice, ar.WBS1
                              ORDER BY COALESCE(ar.InvoiceDate, ar.DueDate) DESC) AS rn
    FROM [{_catalog}].dbo.AR ar
    WHERE ar.ClientID IS NOT NULL AND LTRIM(RTRIM(ar.ClientID)) <> ''
) arx
  ON arx.Invoice = l.Invoice AND arx.WBS1 = l.WBS1 AND arx.rn = 1
LEFT JOIN [{_catalog}].dbo.Clendor cc ON cc.ClientID = arx.ClientID
```

### Line 506
```csharp
            var prefixes = ExtractAccountPrefixes(distinct);
            if (prefixes.Count == 0)
                return result;

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT Account, Name
FROM [{_catalog}].dbo.CA
WHERE LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ({MakePlaceholders(prefixes.Count)});";
            foreach (var prefix in prefixes)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = prefix });

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (r.IsDBNull(0))
                    continue;
                var account = (Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture) ?? "").Trim();
                var name = r.IsDBNull(1) ? "" : (Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture) ?? "").Trim();
                var prefix = AccountPrefix4(account);
                if (prefix.Length == 0 || result.ContainsKey(prefix))
                    continue;
                result[prefix] = name;
            }
            return result;
        }

        private static string FormatAccountLabel(string account, IReadOnlyDictionary<string, string> accountNames)
        {
            var prefix = AccountPrefix4(account);
            if (prefix.Length > 0
                && accountNames.TryGetValue(prefix, out var name)
                && !string.IsNullOrWhiteSpace(name))
                return $"{name} ({account})";
```

### Line 555
```csharp
            var prefixes = ExtractAccountPrefixes(revenueAccounts);
            if (prefixes.Count == 0)
                return null;

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT MAX(Period)
FROM [{_catalog}].dbo.LedgerAR
WHERE TransType = 'IN'
  AND LEFT(LTRIM(RTRIM(COALESCE(Account,''))), 4) IN ({MakePlaceholders(prefixes.Count)})
  AND (? IS NULL OR Org = ?);";
            foreach (var prefix in prefixes)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = prefix });
            AddNullableOrgParameters(cmd, orgFilter);
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var value = cmd.ExecuteScalar();
            if (value == null || value == DBNull.Value)
                return null;
            var period = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return period > 0 ? period : null;
        }

        // Pull the leading 4-char account prefix off each configured value so the
        // SQL predicate stays format-agnostic. Drops any value that can't yield a
        // 4-char numeric prefix (e.g. a stray empty or whitespace entry).
        private static List<string> ExtractAccountPrefixes(IReadOnlyList<string> revenueAccounts)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>(revenueAccounts.Count);
            foreach (var raw in revenueAccounts)
            {
                var prefix = AccountPrefix4(raw);
                if (prefix.Length == 0)
                    continue;
                if (seen.Add(prefix))
                    result.Add(prefix);
```

### Line 600
```csharp
        }

        private int? LoadMaxPostedPeriod(OdbcConnection cn, string? orgFilter, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $"SELECT MAX(Period) FROM [{_catalog}].dbo.GLSummary WHERE (? IS NULL OR Org = ?);";
            AddNullableOrgParameters(cmd, orgFilter);
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var value = cmd.ExecuteScalar();
            if (value == null || value == DBNull.Value)
                return null;
            var period = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return period > 0 ? period : null;
        }

        private OdbcConnection CreateConnection()
        {
            var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
            var user = _odbcOptions.User ?? string.Empty;
            var pwd = _odbcOptions.Password ?? string.Empty;
            var factory = new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>());
            return factory.Create();
        }

        private static void AddNullableOrgParameters(OdbcCommand cmd, string? orgFilter)
        {
            object value = string.IsNullOrWhiteSpace(orgFilter) ? DBNull.Value : orgFilter.Trim();
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = value });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = value });
        }

        private static string? NormalizeOrgFilter(string? orgFilter)
            => string.IsNullOrWhiteSpace(orgFilter) ? null : orgFilter.Trim();

        private static List<int> BuildMonthPeriods(DateTime fromDate, DateTime toDate)
        {
```

## Kor.Operations.Business\GlProfitLossService.cs

### Line 44
```csharp

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                var tableNameLike = string.IsNullOrWhiteSpace(_financialsOptions.PnLGlTableNameLike)
                    ? "%Income Statement%"
                    : _financialsOptions.PnLGlTableNameLike;
                cmd.CommandText = $"SELECT TableNo, TableName, FilterOrg, FilterCode FROM [{catalog}].dbo.GLTable WHERE TableName LIKE ? ORDER BY TableNo;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = tableNameLike });

                using var reg = cancelToken.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                var list = new List<GlTableInfo>();
                while (r.Read())
                {
                    cancelToken.ThrowIfCancellationRequested();
                    list.Add(new GlTableInfo
                    {
                        TableNo = r.IsDBNull(0) ? (short)0 : r.GetInt16(0),
                        TableName = r.IsDBNull(1) ? "" : r.GetString(1),
                        FilterOrg = r.IsDBNull(2) ? "" : r.GetString(2),
                        FilterCode = r.IsDBNull(3) ? "" : r.GetString(3),
                    });
                }

                return (IReadOnlyList<GlTableInfo>)list;
            }, cancelToken).ConfigureAwait(false);
        }

        public sealed record BuildResult(
            DataTable Table,
            int[] Periods,
            string[] PeriodColumnNames,
            decimal[] NetIncomeTrendValues,
            decimal[] RevenueTrendValues,
            decimal[] ExpenseTrendValues,
            string[] TrendLabels,
            int? MaxPostedPeriod);
```

### Line 480
```csharp
            cmd.CommandTimeout = SqlTimeouts.Batch;

            var whereOrg = "";
            if (!string.IsNullOrWhiteSpace(orgFilter))
                whereOrg = " AND Org = ? ";

            cmd.CommandText = $@"
SELECT TOP 1 Period
FROM [{catalog}].dbo.GLSummary
WHERE Period >= ? AND Period <= ?
{whereOrg};";
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = minPeriod });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = maxPeriod });
            if (!string.IsNullOrWhiteSpace(orgFilter))
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = orgFilter.Trim() });

            using var reg = cancelToken.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                cancelToken.ThrowIfCancellationRequested();
                return !r.IsDBNull(0);
            }

            return false;
        }

        private static int? LoadMaxGlPeriodSync(OdbcConnection cn, string catalog, string? orgFilter, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            var whereOrg = string.IsNullOrWhiteSpace(orgFilter) ? "" : " WHERE Org = ?";
            cmd.CommandText = $"SELECT MAX(Period) FROM [{catalog}].dbo.GLSummary{whereOrg};";
            if (!string.IsNullOrWhiteSpace(orgFilter))
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = orgFilter.Trim() });
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var v = cmd.ExecuteScalar();
```

### Line 506
```csharp

        private static int? LoadMaxGlPeriodSync(OdbcConnection cn, string catalog, string? orgFilter, CancellationToken ct)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            var whereOrg = string.IsNullOrWhiteSpace(orgFilter) ? "" : " WHERE Org = ?";
            cmd.CommandText = $"SELECT MAX(Period) FROM [{catalog}].dbo.GLSummary{whereOrg};";
            if (!string.IsNullOrWhiteSpace(orgFilter))
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = orgFilter.Trim() });
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var v = cmd.ExecuteScalar();
            if (v == null || v == DBNull.Value) return null;
            var raw = Convert.ToInt32(v, CultureInfo.InvariantCulture);
            return raw > 0 ? raw : (int?)null;
        }

        private static string PeriodColumnHeader(int period)
        {
            // Prefer deterministic labels. CFGPostControl dates appear inconsistent in this environment.
            var y = period / 100;
            var m = period % 100;
            if (m < 1 || m > 12) return period.ToString(CultureInfo.InvariantCulture);
            var d = new DateTime(y, m, 1);
            return $"{d.ToString("MMM-yy", CultureInfo.InvariantCulture)} ({period})";
        }

        private static string PeriodChartLabel(int period)
        {
            var y = period / 100;
            var m = period % 100;
            if (m < 1 || m > 12) return period.ToString(CultureInfo.InvariantCulture);
            var d = new DateTime(y, m, 1);
            // Two-line label reads cleanly under narrow bars.
            return $"{d.ToString("MMM", CultureInfo.InvariantCulture)}\n{d.ToString("yy", CultureInfo.InvariantCulture)}";
        }

        private sealed class SectionDef
```

### Line 557
```csharp

        private static List<SectionDef> LoadSections(OdbcConnection cn, short tableNo, string catalog, CancellationToken cancelToken)
        {
            // Sections come from GLParentHeading/GLParentGroup.
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT h.GLGroup, pg.Description, h.SortOrder, pg.GroupType
FROM [{catalog}].dbo.GLParentHeading h
JOIN [{catalog}].dbo.GLParentGroup pg
  ON pg.Code = h.GLGroup
WHERE h.TableNo = ?
ORDER BY h.SortOrder;";
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = tableNo });

            using var reg = cancelToken.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            var list = new List<SectionDef>();
            while (r.Read())
            {
                cancelToken.ThrowIfCancellationRequested();
                list.Add(new SectionDef
                {
                    Code = r.IsDBNull(0) ? (short)0 : r.GetInt16(0),
                    Description = r.IsDBNull(1) ? "" : r.GetString(1),
                    SortOrder = r.IsDBNull(2) ? (short)0 : r.GetInt16(2),
                    GroupType = r.IsDBNull(3) ? (short)0 : r.GetInt16(3),
                });
            }

            return list;
        }

        private static List<LineDef> LoadLineGroups(OdbcConnection cn, short tableNo, string catalog, CancellationToken cancelToken)
        {
            // Line items come from GLParentDetail (child group IDs) and GLGroup/GLGroupHeading.
            using var cmd = cn.CreateCommand();
```

### Line 589
```csharp

        private static List<LineDef> LoadLineGroups(OdbcConnection cn, short tableNo, string catalog, CancellationToken cancelToken)
        {
            // Line items come from GLParentDetail (child group IDs) and GLGroup/GLGroupHeading.
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT d.DetailGroupID AS ChildGroupId,
       g.Description,
       COALESCE(gh.SortOrder, 0) AS SortOrder,
       d.GLGroup AS ParentSectionId
FROM [{catalog}].dbo.GLParentDetail d
JOIN [{catalog}].dbo.GLGroup g
  ON g.Code = d.DetailGroupID
LEFT JOIN [{catalog}].dbo.GLGroupHeading gh
  ON gh.TableNo = d.TableNo AND gh.GLGroup = d.DetailGroupID
WHERE d.TableNo = ?
ORDER BY ParentSectionId, SortOrder, g.Description;";
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = tableNo });

            using var reg = cancelToken.Register(() => { try { cmd.Cancel(); } catch { } });
            var list = new List<LineDef>();
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    cancelToken.ThrowIfCancellationRequested();
                    list.Add(new LineDef
                    {
                        Code = r.IsDBNull(0) ? (short)0 : r.GetInt16(0),
                        Description = r.IsDBNull(1) ? "" : r.GetString(1),
                        SortOrder = r.IsDBNull(2) ? (short)0 : Convert.ToInt16(r.GetValue(2), CultureInfo.InvariantCulture),
                        ParentSectionId = r.IsDBNull(3) ? null : (short?)r.GetInt16(3),
                    });
                }
            }

```

### Line 624
```csharp
            }

            // If no parent detail exists for this table, fall back to all groups in GLGroupHeading for the table.
            if (list.Count == 0)
            {
                cmd.Parameters.Clear();
                cmd.CommandText = $@"
SELECT gh.GLGroup, g.Description, gh.SortOrder
FROM [{catalog}].dbo.GLGroupHeading gh
JOIN [{catalog}].dbo.GLGroup g
  ON g.Code = gh.GLGroup
WHERE gh.TableNo = ?
ORDER BY gh.SortOrder, g.Description;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = tableNo });

                using (var r2 = cmd.ExecuteReader())
                {
                    while (r2.Read())
                    {
                        cancelToken.ThrowIfCancellationRequested();
                        list.Add(new LineDef
                        {
                            Code = r2.IsDBNull(0) ? (short)0 : r2.GetInt16(0),
                            Description = r2.IsDBNull(1) ? "" : r2.GetString(1),
                            SortOrder = r2.IsDBNull(2) ? (short)0 : r2.GetInt16(2),
                            ParentSectionId = null
                        });
                    }
                }
            }

            return list;
        }

        private static Dictionary<(short GlGroup, int Period), decimal> LoadAmountsByGroupAndPeriod(
            string catalog,
            OdbcConnection cn,
```

### Line 676
```csharp

            var whereOrg = "";
            if (!string.IsNullOrWhiteSpace(orgFilter))
                whereOrg = " AND s.Org = ? ";

            // Normalize accounts for BETWEEN comparisons (lexical safety).
            cmd.CommandText = $@"
SELECT gd.GLGroup,
       s.Period,
       s.Org,
       SUM(s.Amount) AS Amount
FROM [{catalog}].dbo.GLSummary s
JOIN [{catalog}].dbo.GLGroupDetail gd
  ON gd.TableNo = ?
 AND RIGHT(REPLICATE('0', 13) + s.Account, 13) >= RIGHT(REPLICATE('0', 13) + gd.StartAccount, 13)
 AND RIGHT(REPLICATE('0', 13) + s.Account, 13) <= RIGHT(REPLICATE('0', 13) + gd.EndAccount, 13)
WHERE s.Period >= ? AND s.Period <= ?
{whereOrg}
GROUP BY gd.GLGroup, s.Period, s.Org;";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.SmallInt, Value = tableNo });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = minPeriod });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Int, Value = maxPeriod });
            if (!string.IsNullOrWhiteSpace(orgFilter))
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = orgFilter.Trim() });

            using var reg = cancelToken.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            var dict = new Dictionary<(short, int), decimal>();
            while (r.Read())
            {
                cancelToken.ThrowIfCancellationRequested();

                if (r.IsDBNull(0) || r.IsDBNull(1) || r.IsDBNull(3))
                    continue;

                var group = r.GetInt16(0);
```

### Line 774
```csharp
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;

                var whereOrg = string.IsNullOrWhiteSpace(orgFilter) ? "" : " AND l.Org = ? ";
                var convertUsaToCad = string.IsNullOrWhiteSpace(orgFilter);
                var usdToCadRate = (decimal)OrgFx.ParseUsdToCadRate(_financialsOptions.BilledUsdToCadRate);
                cmd.CommandText = $@"
SELECT
    l.Source,
    l.Period,
    MAX(l.TransDate) AS TransDate,
    COALESCE(NULLIF(l.Invoice, ''), NULLIF(l.Voucher, ''), NULLIF(l.RefNo, ''), '(none)') AS DocumentNo,
    COALESCE(
        NULLIF(cc.Name, ''),
        NULLIF(cv.Name, ''),
        NULLIF(LTRIM(RTRIM(COALESCE(em.FirstName, '') + ' ' + COALESCE(em.LastName, ''))), ''),
        NULLIF(l.Vendor, ''),
        NULLIF(l.Employee, ''),
        '(unmapped)') AS Counterparty,
    l.Account,
    l.Org,
    l.TransType,
    MAX(COALESCE(NULLIF(l.Desc1, ''), NULLIF(l.Desc2, ''), '')) AS Description,
    SUM(l.Amount) AS Amount,
    COUNT(*) AS EntryCount
FROM
(
    SELECT 'AR' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate, l.Desc1, l.Desc2, l.Amount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{catalog}].dbo.LedgerAR l
    WHERE l.Period = ? {whereOrg}
    UNION ALL
    SELECT 'AP' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate, l.Desc1, l.Desc2, l.Amount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{catalog}].dbo.LedgerAP l
    WHERE l.Period = ? {whereOrg}
    UNION ALL
    SELECT 'EX' AS Source, l.Period, l.WBS1, l.Account, l.Org, l.TransType, l.RefNo, l.TransDate, l.Desc1, l.Desc2, l.Amount, l.Invoice, l.Voucher, l.Employee, l.Vendor
    FROM [{catalog}].dbo.LedgerEX l
```

## Kor.Operations.Data\SqlTransmittalsStore.cs

### Line 187
```csharp
       AppVersion = COALESCE(@AppVersion, AppVersion)
 WHERE Id = @Id;";

                await using var cn = await _openConnectionAsync(innerCt);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.Batch;
                AddParameter(cmd, "@Id", transmittalId);
                AddParameter(cmd, "@SentAt", sentUtc);
                AddParameter(cmd, "@SentBy", sentBy ?? string.Empty);
                AddParameter(cmd, "@AppVersion", appVersion);

                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        public async Task UpdateEmailStatusAsync(
            Guid transmittalId,
            DateTime? sentAtUtc,
            string? errorMessage,
            CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
UPDATE dbo.Transmittals
   SET EmailSentAt = @EmailSentAt,
       EmailSendError = @EmailSendError
 WHERE Id = @Id;";

                await using var cn = await _openConnectionAsync(innerCt);
                await EnsureEmailStatusColumnsAsync(cn, innerCt);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.Batch;
```

### Line 216
```csharp
 WHERE Id = @Id;";

                await using var cn = await _openConnectionAsync(innerCt);
                await EnsureEmailStatusColumnsAsync(cn, innerCt);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.Batch;
                AddParameter(cmd, "@Id", transmittalId);
                AddParameter(cmd, "@EmailSentAt", sentAtUtc);
                AddParameter(cmd, "@EmailSendError", string.IsNullOrWhiteSpace(errorMessage) ? null : TrimToLength(errorMessage, 500));

                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        // ---------------------------
        // Dashboard query methods
        // ---------------------------

        public async Task<IReadOnlyList<TransmittalSummary>> SearchSummaryAsync(
            string? text,
            DateTime? startUtc,
            DateTime? endUtc,
            string? typeFilter = null,
            bool includeSharePointUrlInSearch = false,
            int take = 200,
            CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
WITH O AS (
    SELECT oe.TransmittalId, COUNT_BIG(*) AS OpenCount
    FROM dbo.OpenEvents oe
    GROUP BY oe.TransmittalId
),
```

### Line 279
```csharp

                var list = new List<TransmittalSummary>(Math.Max(32, take));

                await using var cn = await _openConnectionAsync(innerCt);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.UiFacing;

                string? textLike = null;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var escaped = text.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
                    textLike = $"%{escaped}%";
                }

                AddParameter(cmd, "@Take", take);
                AddParameter(cmd, "@Text", text);
                AddParameter(cmd, "@TextLike", textLike);
                AddParameter(cmd, "@Start", startUtc);
                AddParameter(cmd, "@End", endUtc);
                AddParameter(cmd, "@TypeFilter", typeFilter);
                AddParameter(cmd, "@IncludeSharePointUrlInSearch", includeSharePointUrlInSearch ? 1 : 0);

                using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await rd.ReadAsync(innerCt))
                {
                    var row = new TransmittalSummary
                    {
                        Id = rd.GetGuid(0),
                        ProjectNo = rd.GetStringOrEmpty(1),
                        Subject = rd.GetStringOrEmpty(2),
                        SharePointUrl = rd.GetStringOrEmpty(3),
                        CreatedAt = rd.GetDateTimeOrNull(4),
                        SentAt = rd.GetDateTimeOrNull(5),
                        Type = rd.IsDBNull(6) ? "Transmittal" : rd.GetString(6),
                        OpenCount = rd.GetInt64OrDefault(7),
```

### Line 348
```csharp
SELECT Subject   AS Val, 2 AS Ord FROM S
ORDER BY Ord, Val;";

                var list = new List<string>(projectTake + subjectTake);
                await using var cn = await _openConnectionAsync(innerCt);
                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                AddParameter(cmd, "@ProjectTake", projectTake);
                AddParameter(cmd, "@SubjectTake", subjectTake);
                AddParameter(cmd, "@TextLike", $"%{text}%");

                using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await rd.ReadAsync(innerCt))
                    list.Add(rd.GetStringOrEmpty(0));

                return (IReadOnlyList<string>)list;
            }, ct);
        }

        public async Task<IReadOnlyList<ActivityRow>> LoadActivityAsync(
            Guid transmittalId,
            int take = 200,
            CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
SELECT TOP (@Take)
       'Open'  AS Kind,
       oe.OpenedAt AS OccurredAt,
       oe.RecipientEmail,
       oe.ClientIp,
       oe.UserAgent,
       CAST(NULL AS nvarchar(1024)) AS Referer
FROM dbo.OpenEvents oe
WHERE oe.TransmittalId = @Tid
```

### Line 399
```csharp

                var list = new List<ActivityRow>(Math.Max(32, take));

                await using var cn = await _openConnectionAsync(innerCt);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                AddParameter(cmd, "@Take", take);
                AddParameter(cmd, "@Tid", transmittalId);

                using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await rd.ReadAsync(innerCt))
                {
                    var row = new ActivityRow
                    {
                        Kind = rd.GetStringOrEmpty(0),
                        OccurredAt = rd.IsDBNull(1) ? DateTime.MinValue : rd.GetDateTime(1),
                        RecipientEmail = rd.GetStringOrEmpty(2),
                        ClientIp = rd.GetStringOrEmpty(3),
                        UserAgent = rd.GetStringOrEmpty(4),
                        Referer = rd.GetStringOrNull(5)
                    };
                    list.Add(row);
                }

                return (IReadOnlyList<ActivityRow>)list;
            }, ct);
        }

        private static void AddParameter(DbCommand cmd, string name, object? value)
        {
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(parameter);
        }
```

### Line 446
```csharp
IF COL_LENGTH('dbo.Transmittals', 'EmailSendError') IS NULL
    ALTER TABLE dbo.Transmittals ADD EmailSendError NVARCHAR(500) NULL;";

            try
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.Batch;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                // ALTER TABLE may fail if the app user lacks DDL permission.
                // Columns must be added via a manual migration script.
                // Swallow here so UpdateEmailStatusAsync can still attempt the UPDATE.
            }
        }

        private static string TrimToLength(string value, int maxLength)
            => value.Length <= maxLength ? value : value[..maxLength];

        private static async Task InsertTransmittalAsync(
            DbConnection cn,
            DbTransaction? transaction,
            Guid id,
            string projectNo,
            string subject,
            string driveId,
            string itemId,
            string sharePointUrl,
            DateTime createdUtc,
            string createdBy,
            string? appVersion,
            string type,
            CancellationToken ct)
        {
            const string sql = @"
```

### Line 484
```csharp
    (Id, ProjectNo, Subject, DriveId, ItemId, SharePointUrl, CreatedAt, CreatedBy, AppVersion, [Type])
VALUES
    (@Id, @ProjectNo, @Subject, @DriveId, @ItemId, @SharePointUrl, @CreatedUtc, @CreatedBy, @AppVersion, @Type);";

            await using var cmd = cn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            cmd.CommandTimeout = SqlTimeouts.Batch;
            AddParameter(cmd, "@Id", id);
            AddParameter(cmd, "@ProjectNo", projectNo);
            AddParameter(cmd, "@Subject", subject);
            AddParameter(cmd, "@DriveId", driveId);
            AddParameter(cmd, "@ItemId", itemId);
            AddParameter(cmd, "@SharePointUrl", sharePointUrl);
            AddParameter(cmd, "@CreatedUtc", createdUtc);
            AddParameter(cmd, "@CreatedBy", createdBy);
            AddParameter(cmd, "@AppVersion", appVersion);
            AddParameter(cmd, "@Type", type ?? "Transmittal");

            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task InsertRecipientsAsync(
            DbConnection cn,
            DbTransaction? transaction,
            Guid transmittalId,
            IEnumerable<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)> recips,
            CancellationToken ct)
        {
            const string sql = @"
INSERT INTO dbo.TransmittalRecipients
    (Id, TransmittalId, Email, Kind, LinkId, PersonalShareLink, LastActivityAt)
VALUES
    (@Id, @TransmittalId, @Email, @Kind, @LinkId, @PersonalShareLink, NULL);";

            foreach (var r in recips)
            {
```

### Line 517
```csharp
    (@Id, @TransmittalId, @Email, @Kind, @LinkId, @PersonalShareLink, NULL);";

            foreach (var r in recips)
            {
                await using var cmd = cn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.Batch;
                AddParameter(cmd, "@Id", Guid.NewGuid());
                AddParameter(cmd, "@TransmittalId", transmittalId);
                AddParameter(cmd, "@Email", r.Email ?? string.Empty);
                AddParameter(cmd, "@Kind", r.Kind ?? string.Empty);
                AddParameter(cmd, "@LinkId", r.LinkId);
                AddParameter(cmd, "@PersonalShareLink", r.PersonalShareLink);

                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
    }

    // ---------- Small DTOs for dashboard ----------

    public sealed class TransmittalSummary
    {
        public Guid Id { get; set; }
        public string ProjectNo { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string SharePointUrl { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public long OpenCount { get; set; }
        public long ClickCount { get; set; }

        // Used by the dashboard Type column and row colouring
        public string Type { get; set; } = "Transmittal";
    }

```

