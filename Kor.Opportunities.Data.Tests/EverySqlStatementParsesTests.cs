using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Every SQL statement held as a constant in Kor.Opportunities.Data (a const string field whose name ends in "Sql") is handed
// to SQL Server with SET PARSEONLY ON: parsed, never run, nothing written. Added 2026-10-22 after V28 shipped a status
// query that aliased a table as "fetch", a reserved word, so it failed to parse for every pursuit and the review strip on
// My Pursuits never loaded; Rory hit it on his first morning. Checks SYNTAX only: a wrong column or table name, a missing
// parameter, or a wrong result shape still passes, and so does SQL built at run time (interpolated strings, string
// builders) and fragments that are spliced into a larger statement (they are skipped, see Standalone).
public sealed class EverySqlStatementParsesTests
{
    private static readonly Regex Standalone = new(@"^\s*(--[^\n]*\n\s*)*(SET|SELECT|WITH|UPDATE|INSERT|DELETE|MERGE|DECLARE|IF|BEGIN|EXEC|;)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Parameter = new(@"(?<![@\w])@(\w+)", RegexOptions.CultureInvariant);
    private static readonly Regex DeclaredInText = new(
        @"(?:DECLARE|,)\s*@(\w+)\s+(?:TABLE|bigint|int|smallint|tinyint|bit|n?varchar|n?char|datetimeoffset|datetime2?|date|time|decimal|numeric|float|money|uniqueidentifier|sql_variant|varbinary)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<(string Name, string Sql)> Statements() =>
        typeof(Kor.Opportunities.Data.Crm.SqlPursuitGrabStore).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.EndsWith("Sql", StringComparison.Ordinal))
                .Select(f => ($"{t.Name}.{f.Name}", (string)f.GetRawConstantValue()!)))
            .Where(s => Standalone.IsMatch(s.Item2));

    [Fact]
    public void There_are_statements_to_check()
        => Assert.True(Statements().Count() >= 20, $"EXPECTED at least 20 SQL constants; ACTUAL {Statements().Count()}");

    [DatabaseFact]
    public void Every_sql_constant_parses()
    {
        using var con = new SqlConnection(Environment.GetEnvironmentVariable(DatabaseFactAttribute.Variable));
        con.Open();
        var failures = new List<string>();
        foreach (var (name, sql) in Statements())
        {
            using (var on = new SqlCommand("SET PARSEONLY ON;", con)) on.ExecuteNonQuery();
            try
            {
                // The caller's parameters are declared first: an undeclared one makes SQL Server report follow-on syntax
                // errors that are not real. A variable the statement declares itself is followed by its type, so it is left out.
                var declared = new HashSet<string>(DeclaredInText.Matches(sql).Select(m => m.Groups[1].Value), StringComparer.OrdinalIgnoreCase);
                var parameters = Parameter.Matches(sql).Select(m => m.Groups[1].Value)
                    .Where(p => !declared.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var head = parameters.Count == 0 ? "" : "DECLARE " + string.Join(", ", parameters.Select(p => "@" + p + " sql_variant")) + ";\n";
                // A template slot the code fills before running ("IN (/*PROJECT_IDS*/)") gets a stand-in value.
                var filled = Regex.Replace(sql, @"\(\s*/\*[A-Z_]+\*/\s*\)", "(0)");
                using var cmd = new SqlCommand(head + filled, con) { CommandTimeout = 30 };
                cmd.ExecuteNonQuery();
            }
            catch (SqlException ex)
            {
                foreach (SqlError e in ex.Errors) failures.Add($"{name}: [{e.Number}] {e.Message}");
            }
            finally
            {
                using var off = new SqlCommand("SET PARSEONLY OFF;", con);
                off.ExecuteNonQuery();
            }
        }
        Assert.True(failures.Count == 0, "SQL that does not parse:\n" + string.Join("\n", failures));
    }

    // The statement that failed on 2026-10-22, run for real against a pursuit that has a tender.
    [DatabaseFact]
    public async Task The_review_status_loads_for_a_tender_pursuit()
    {
        var cs = Environment.GetEnvironmentVariable(DatabaseFactAttribute.Variable)!;
        long engagementId;
        await using (var con = new SqlConnection(cs))
        {
            await con.OpenAsync();
            // A real pursuit, never one of the AUDITTEST rows the lifecycle tests create and delete in parallel (picking the
            // newest once caught one mid-delete).
            await using var cmd = new SqlCommand(@"SELECT TOP (1) e.Id FROM opportunities.CrmEngagements e
JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
WHERE e.Stage IN (1, 3) AND o.OpportunityKey NOT LIKE N'AUDITTEST%' AND ISNULL(e.OwnerStaffId, N'') NOT LIKE N'audittest%'
ORDER BY e.Id", con);
            engagementId = (long)(await cmd.ExecuteScalarAsync())!;
        }
        var status = await new Kor.Opportunities.Data.Crm.SqlPursuitReviewStore(cs).StatusAsync(engagementId, CancellationToken.None);
        Assert.NotNull(status);
    }
}
