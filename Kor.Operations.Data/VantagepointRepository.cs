#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Kor.Operations.Data
{
    /// <summary>
    /// Thin ODBC repository for Deltek Vantagepoint.
    /// </summary>
    public sealed class VantagepointRepository
    {
        private enum QueryTable
        {
            Projects,
            Contacts,
            Clendor,
            Employees
        }

        private enum QueryOrderBy
        {
            ProjectsByActivity,
            ContactsByName,
            EmployeesByName
        }

        private readonly IOdbcConnectionFactory _factory;

        public VantagepointRepository(IOdbcConnectionFactory factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        // --------------------------------------------------------------------
        //  Models
        // --------------------------------------------------------------------

        public enum PersonSource
        {
            Contact,
            Employee
        }

        /// <summary>Row model for the contact picker.</summary>
        public sealed class ContactRow
        {
            public string Name { get; set; } = "";
            public string Email { get; set; } = "";
            public string Company { get; set; } = "";
            public string Title { get; set; } = "";
            public PersonSource Source { get; set; } = PersonSource.Contact;
            public string? EmployeeId { get; set; } // from EMMain.Employee if available
        }

        // --------------------------------------------------------------------
        //  PROJECTS (kept for compatibility; safe no-op unless you call it)
        // --------------------------------------------------------------------
        public async Task<IReadOnlyList<(string Wbs1, string Display)>> GetProjectsAsync(
            bool onlyActive = true,
            int yearsBack = 3,
            int max = 2000,
            CancellationToken ct = default)
        {
            using var cn = _factory.Create();
#if NET6_0_OR_GREATER
            await cn.OpenAsync(ct);
#else
            cn.Open();
#endif
            var cutoff = DateTime.UtcNow.AddYears(-Math.Abs(yearsBack));

            // SELECT ordered by most-recent activity
            var sql = new StringBuilder($@"
                SELECT PR.WBS1, PR.Name, COALESCE(PR.ModDate, PR.CreateDate) AS ActivityDate
                FROM {GetTableName(QueryTable.Projects)} PR
                WHERE PR.WBS1 IS NOT NULL AND PR.WBS1 <> '' ");

            if (onlyActive) sql.Append("AND PR.Status = 'A' ");
            sql.Append("AND COALESCE(PR.ModDate, PR.CreateDate) >= ? ");
            sql.Append(GetOrderByClause(QueryOrderBy.ProjectsByActivity));

            using var cmd = new OdbcCommand(sql.ToString(), cn);
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = cutoff });

#if NET6_0_OR_GREATER
            using var r = await cmd.ExecuteReaderAsync(ct);
#else
            using var r = cmd.ExecuteReader();
#endif
            var rows = new List<(string Wbs1, string Name)>(max * 2);
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();

                var w = (r["WBS1"] as string ?? "").Trim();
                var n = (r["Name"] as string ?? "").Trim();
                if (w.Length == 0) continue;

                rows.Add((w, n));
                if (rows.Count > max * 4) break; // safety valve
            }

            // De-dupe by WBS1 keeping first (newest) occurrence
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dedupOrdered = new List<(string Wbs1, string Display)>(max);

            foreach (var (wbs1, name) in rows)
            {
                if (seen.Add(wbs1))
                {
                    dedupOrdered.Add((wbs1, $"{wbs1} - {name}"));
                    if (dedupOrdered.Count >= max) break;
                }
            }

            return dedupOrdered;
        }

        // --------------------------------------------------------------------
        //  CONTACTS (CRM) - with company via CLENDOR join and active-company gate
        // --------------------------------------------------------------------
        /// <summary>
        /// Returns up to max contacts filtered by query.
        /// Company comes from CLENDOR.Name via LEFT JOIN on Contacts.ClientID.
        /// Excludes contacts whose joined company is not active (Status <> 'A'),
        /// while allowing contacts with no ClientID (no company) to pass through.
        /// </summary>
        public async Task<IReadOnlyList<ContactRow>> SearchContactsAsync(
            string? query, int max = 25000, CancellationToken ct = default)
        {
            using var cn = _factory.Create();
#if NET6_0_OR_GREATER
            await cn.OpenAsync(ct);
#else
            cn.Open();
#endif

            // IMPORTANT: LEFT JOIN so contacts with no ClientID are still included.
            // Active-company filter: (c.ClientID IS NULL OR cl.Status = 'A')
            var sql = new StringBuilder($@"
                SELECT
                    COALESCE(c.FirstName, '') AS FirstName,
                    COALESCE(c.LastName,  '') AS LastName,
                    COALESCE(c.EMail,     '') AS EMail,
                    COALESCE(cl.Name,     '') AS Company,
                    COALESCE(c.Title,     '') AS Title
                FROM {GetTableName(QueryTable.Contacts)} c
                LEFT JOIN {GetTableName(QueryTable.Clendor)} cl
                    ON cl.ClientID = c.ClientID
                WHERE COALESCE(c.EMail, '') <> ''
                  AND (c.ClientID IS NULL OR cl.Status = 'A')");

            var q = (query ?? "").Trim();
            bool hasFilter = q.Length >= 2;
            if (hasFilter)
            {
                sql.Append(@"
                  AND (
                        UPPER(c.FirstName) LIKE UPPER(?) OR
                        UPPER(c.LastName)  LIKE UPPER(?) OR
                        UPPER(cl.Name)     LIKE UPPER(?) OR
                        UPPER(c.EMail)     LIKE UPPER(?) OR
                        UPPER(c.Title)     LIKE UPPER(?)
                      )");
            }

            sql.Append(GetOrderByClause(QueryOrderBy.ContactsByName));

            using var cmd = new OdbcCommand(sql.ToString(), cn);

            if (hasFilter)
            {
                string like = "%" + q + "%";
                // FirstName
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
                // LastName
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
                // Company (Clendor.Name)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
                // Email
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
                // Title
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
            }

            var results = new List<ContactRow>(Math.Min(max, 25000));

#if NET6_0_OR_GREATER
            using var r = await cmd.ExecuteReaderAsync(ct);
#else
            using var r = cmd.ExecuteReader();
#endif
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();

                var first = (r["FirstName"] as string ?? "").Trim();
                var last = (r["LastName"] as string ?? "").Trim();
                var email = (r["EMail"] as string ?? "").Trim();
                var comp = (r["Company"] as string ?? "").Trim();
                var title = (r["Title"] as string ?? "").Trim();

                if (email.Length == 0) continue;

                string display = NormalizeName($"{first} {last}".Trim());
                if (display.Length == 0) display = email;

                results.Add(new ContactRow
                {
                    Name = display,
                    Email = email,
                    Company = comp,
                    Title = title,
                    Source = PersonSource.Contact
                });

                if (results.Count >= max) break;
            }

            return results;
        }

        // --------------------------------------------------------------------
        //  EMPLOYEES (internal) - EMMain
        // --------------------------------------------------------------------
        /// <summary>
        /// Returns up to max internal employees filtered by query from EMMain.
        /// Uses FirstName, LastName, PreferredName, EMail, Title, Employee.
        /// Company is HomeCompany if present, otherwise defaults to KOR Structural.
        /// </summary>
        public async Task<IReadOnlyList<ContactRow>> SearchEmployeesAsync(
            string? query, int max = 25000, CancellationToken ct = default)
        {
            using var cn = _factory.Create();
#if NET6_0_OR_GREATER
            await cn.OpenAsync(ct);
#else
    cn.Open();
#endif

            var sql = new StringBuilder($@"
        SELECT TOP 25000
               COALESCE(e.FirstName, '')      AS FirstName,
               COALESCE(e.LastName,  '')      AS LastName,
               COALESCE(e.PreferredName, '')  AS PreferredName,
               COALESCE(e.EMail,     '')      AS EMail,
               COALESCE(e.Title,     '')      AS Title,
               COALESCE(e.HomeCompany,'')     AS HomeCompany,
               COALESCE(e.Employee,  '')      AS EmployeeId
        FROM {GetTableName(QueryTable.Employees)} e
        WHERE COALESCE(e.EMail, '') <> ''");

            var q = (query ?? "").Trim();
            bool hasFilter = q.Length >= 2;
            if (hasFilter)
            {
                // Note the SQL Server string concatenation using +
                sql.Append(@"
          AND (
                UPPER(e.FirstName + ' ' + e.LastName) LIKE UPPER(?) OR
                UPPER(e.FirstName) LIKE UPPER(?) OR
                UPPER(e.LastName)  LIKE UPPER(?) OR
                UPPER(e.PreferredName) LIKE UPPER(?) OR
                UPPER(e.EMail)     LIKE UPPER(?) OR
                UPPER(e.Title)     LIKE UPPER(?) OR
                UPPER(e.Employee)  LIKE UPPER(?)
              )");
            }

            sql.Append(GetOrderByClause(QueryOrderBy.EmployeesByName));

            using var cmd = new OdbcCommand(sql.ToString(), cn);

            if (hasFilter)
            {
                string like = "%" + q + "%";
                // Full name
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
                // FirstName
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
                // LastName
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
                // PreferredName
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
                // Email
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
                // Title
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
                // Employee ID
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = like });
            }

            var results = new List<ContactRow>(Math.Min(max, 25000));

#if NET6_0_OR_GREATER
            using var r = await cmd.ExecuteReaderAsync(ct);
#else
    using var r = cmd.ExecuteReader();
#endif
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();

                var first = (r["FirstName"] as string ?? "").Trim();
                var last = (r["LastName"] as string ?? "").Trim();
                var pref = (r["PreferredName"] as string ?? "").Trim();
                var email = (r["EMail"] as string ?? "").Trim();
                var title = (r["Title"] as string ?? "").Trim();
                var company = (r["HomeCompany"] as string ?? "").Trim();
                var empId = (r["EmployeeId"] as string ?? "").Trim();

                if (email.Length == 0) continue;

                string display = NormalizeName($"{first} {last}".Trim());
                if (display.Length == 0 && pref.Length > 0) display = pref;
                if (display.Length == 0) display = email;

                var comp = company.Length > 0 ? company : "KOR Structural";

                results.Add(new ContactRow
                {
                    Name = display,
                    Email = email,
                    Company = comp,
                    Title = title,
                    Source = PersonSource.Employee,
                    EmployeeId = empId
                });

                if (results.Count >= max) break;
            }

            return results;
        }


        // --------------------------------------------------------------------
        //  PEOPLE (union)
        // --------------------------------------------------------------------
        /// <summary>
        /// Unified people search: CRM contacts + internal employees.
        /// UI can de-duplicate by Email as needed.
        /// </summary>
        public async Task<IReadOnlyList<ContactRow>> SearchPeopleAsync(
            string? query, int max = 25000, CancellationToken ct = default)
        {
            // Run in parallel and merge
            var cap = Math.Max(1, Math.Min(max, 25000));

            var contactsTask = SearchContactsAsync(query, cap, ct);
            var employeesTask = SearchEmployeesAsync(query, cap, ct);

            await Task.WhenAll(contactsTask, employeesTask).ConfigureAwait(false);

            // Concatenate; paging/dedup is handled by caller (your ContactPickerWindow)
            // To be friendly, we still cap total size here.
            return contactsTask.Result.Concat(employeesTask.Result).Take(cap).ToList();
        }

        // --------------------------------------------------------------------
        //  Helpers
        // --------------------------------------------------------------------

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            s = s.Trim();

            // Some rows have artifacts like "." or "?" as a last-name placeholder.
            // Strip trailing punctuation-only tokens.
            char[] junk = new[] { '.', '?', '-', '_' };
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                         .Where(p => p.Trim(junk).Length > 0)
                         .ToArray();
            return string.Join(" ", parts);
        }

        private static string GetOrderByClause(QueryOrderBy orderBy) =>
            orderBy switch
            {
                QueryOrderBy.ProjectsByActivity => "ORDER BY ActivityDate DESC, PR.WBS1",
                QueryOrderBy.ContactsByName => " ORDER BY c.LastName, c.FirstName, cl.Name, c.EMail",
                QueryOrderBy.EmployeesByName => " ORDER BY e.LastName, e.FirstName, e.EMail",
                _ => throw new ArgumentException($"Unsupported ORDER BY selection '{orderBy}'.", nameof(orderBy))
            };

        private static string GetTableName(QueryTable table) =>
            table switch
            {
                QueryTable.Projects => "PR",
                QueryTable.Contacts => "Contacts",
                QueryTable.Clendor => "Clendor",
                QueryTable.Employees => "EMMain",
                _ => throw new ArgumentException($"Unsupported table selection '{table}'.", nameof(table))
            };

        // --------------------------------------------------------------------
        //  SCHEMA PEEK (optional; harmless if unused)
        // --------------------------------------------------------------------
        public async Task<string> GetSchemaStringAsync(
            int maxTables = 50, int maxColumnsPerTable = 40, CancellationToken ct = default)
        {
            using var cn = _factory.Create();
#if NET6_0_OR_GREATER
            await cn.OpenAsync(ct);
#else
            cn.Open();
#endif
            DataTable tables = cn.GetSchema("Tables");
            var rows = tables.Select("TABLE_TYPE = 'TABLE' OR TABLE_TYPE = 'BASE TABLE'", "TABLE_SCHEM, TABLE_NAME");

            var sb = new StringBuilder();
            int tCount = 0;
            foreach (var r in rows)
            {
                if (tCount++ >= maxTables) break;

                string schem = (r["TABLE_SCHEM"] as string) ?? (r["TABLE_SCHEMA"] as string) ?? "";
                string name = (r["TABLE_NAME"] as string) ?? "";
                sb.AppendLine($"{(string.IsNullOrEmpty(schem) ? "(default)" : schem)}.{name}");

                var restrictions = new string?[] { null, schem, name, null };
                DataTable cols = cn.GetSchema("Columns", restrictions);

                int c = 0;
                foreach (DataRow cr in cols.Select("", "ORDINAL_POSITION"))
                {
                    if (c++ >= maxColumnsPerTable) break;
                    string colName = (cr["COLUMN_NAME"] as string) ?? "";
                    string dataType = (cr["TYPE_NAME"] as string)
                                   ?? (cr["DATA_TYPE"]?.ToString() ?? "");
                    sb.AppendLine($"    - {colName} : {dataType}");
                }
            }
            return sb.ToString();
        }
    }
}
