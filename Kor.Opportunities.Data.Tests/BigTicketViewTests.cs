using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

public sealed class BigTicketViewTests
{
    [Fact]
    public void GeneralisationRetainsTheExistingSmallWorkPredicateVerbatim()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Kor.Opportunities.Data", "Schema")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var schema = Path.Combine(directory.FullName, "Kor.Opportunities.Data", "Schema");
        var oldSql = File.ReadAllText(Path.Combine(schema, "316_IslandBigTicketView.sql"));
        var newSql = File.ReadAllText(Path.Combine(schema, "338_ApplicationBigTicketCoverage.sql"));
        static string Predicate(string sql)
        {
            var start = sql.IndexOf("o.Name LIKE '%RESIDENTIAL BUILDING PERMIT%'", StringComparison.Ordinal);
            var end = sql.IndexOf("THEN 0 ELSE 1", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            return Regex.Replace(sql[start..end], @"\s+", " ").Trim();
        }
        Assert.Equal(Predicate(oldSql), Predicate(newSql));
    }
}
