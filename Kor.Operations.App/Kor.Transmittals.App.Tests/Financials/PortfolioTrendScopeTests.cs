#nullable enable
using System;
using System.IO;
using Kor.Operations.App.Tests;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials;

/// <summary>
/// The portfolio trend is stored and read per scope (Watchlist / AllActive), with one bucket definition. Covers the
/// bucket mapping, the store's SQL as text, and the composer's insert-before-read wiring as source text. Does not run
/// the SQL, check the live table, or catch a caller that passes the wrong scope constant.
/// </summary>
public sealed class PortfolioTrendScopeTests
{
    [Fact]
    public void Counts_MatchTheScreensFourToThreeBucketMapping()
    {
        var counts = PortfolioHealthCounts.FromLevels(new[]
        {
            DeliveryConfidenceLevel.HighConfidence, DeliveryConfidenceLevel.Watch,
            DeliveryConfidenceLevel.AtRisk, DeliveryConfidenceLevel.Critical,
        });

        Assert.Equal(new PortfolioHealthCounts(Healthy: 2, Watch: 1, Critical: 1, Total: 4), counts);
    }

    [Fact]
    public void Schema_AddsScopeWithWatchlistDefault_AndKeysOnDateAndScope()
    {
        var sql = SqlFinancialPortfolioSnapshotStore.EnsureSchemaSql;
        Assert.Contains("IF COL_LENGTH('dbo.FinancialPortfolioSnapshot', 'Scope') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ADD Scope NVARCHAR(16) NOT NULL CONSTRAINT DF_FinancialPortfolioSnapshot_Scope DEFAULT N'Watchlist'", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (Scope IN (N''Watchlist'', N''AllActive''))", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT UQ_FinancialPortfolioSnapshot_SnapshotDate;", sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (SnapshotDate, Scope)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIQUE (SnapshotDate);", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_DropsTheLiveDateOnlyIndex_AndMigration009CarriesItVerbatim()
    {
        var sql = SqlFinancialPortfolioSnapshotStore.EnsureSchemaSql.Replace("\r\n", "\n").Trim();
        Assert.Contains("DROP INDEX UX_FinancialPortfolioSnapshot_Date ON dbo.FinancialPortfolioSnapshot;", sql, StringComparison.Ordinal);
        var migration = File.ReadAllText(Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(), "db", "KorTransmittals", "009_PortfolioSnapshotScope.sql"))
            .Replace("\r\n", "\n");
        Assert.Contains(sql, migration, StringComparison.Ordinal);
    }

    [Fact]
    public void InsertAndLoad_BothCarryTheScope()
    {
        Assert.Contains("WHERE SnapshotDate = @d AND Scope = @Scope", SqlFinancialPortfolioSnapshotStore.InsertSql, StringComparison.Ordinal);
        Assert.Contains("VALUES(@d, @Scope,", SqlFinancialPortfolioSnapshotStore.InsertSql, StringComparison.Ordinal);
        Assert.Contains("AND Scope = @Scope", SqlFinancialPortfolioSnapshotStore.LoadSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer_RecordsAllActiveBeforeTheEngineRead_AndReportsAFailedInsert()
    {
        var source = File.ReadAllText(Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(), "tools", "MorningBrief", "MorningFacts.cs"));
        var start = source.IndexOf("private static async Task GatherCooAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public static IReadOnlyList<MorningFact> ExecutiveFacts", start, StringComparison.Ordinal);
        var gather = source[start..end];

        var insert = gather.IndexOf("store.TryInsertSnapshotAsync(", StringComparison.Ordinal);
        var read = gather.IndexOf("exec.GetExecutiveSummaryAsync(", StringComparison.Ordinal);
        Assert.True(insert > 0 && read > insert, "The all-active point must be recorded before the engine reads the trend.");
        Assert.Contains("PortfolioSnapshotScope.AllActive,", gather[insert..read], StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", gather[insert..read], StringComparison.Ordinal);
        Assert.Contains("trendNote = ", gather[insert..read], StringComparison.Ordinal);
        Assert.Contains("trendScope: PortfolioSnapshotScope.AllActive", gather[read..], StringComparison.Ordinal);
    }
}
