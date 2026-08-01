#nullable enable
using Kor.Operations.Mcp.Audit;
using Kor.Operations.Mcp.Options;
using Kor.Operations.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Tools;

public sealed class QueryKorDataToolGateTests
{
    [Theory]
    [InlineData("SELECT * FROM OPENQUERY([DELTEK_VP], 'DELETE FROM dbo.X')")]
    [InlineData("SELECT * FROM OPENQUERY([DELTEK_VP], 'UPDATE dbo.X SET Name = ''x''')")]
    [InlineData("SELECT * FROM OPENROWSET('MSDASQL', 'dsn=x', 'SELECT 1')")]
    [InlineData("SELECT * FROM OPENDATASOURCE('MSDASQL', 'dsn=x').dbo.X")]
    public async Task QueryKorDataAsync_RejectsPassThroughProviders(string sql)
    {
        var result = await ExecuteAsync(sql).ConfigureAwait(false);

        Assert.Contains("pass-through is not permitted", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MCP service not configured", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("WITH cte AS (SELECT 1 AS x) DELETE FROM dbo.X")]
    [InlineData("SELECT * INTO dbo.NewTable FROM dbo.Existing")]
    [InlineData("SELECT 1; DELETE FROM dbo.X")]
    public async Task QueryKorDataAsync_RejectsWriteOrBatchedStatements(string sql)
    {
        var result = await ExecuteAsync(sql).ConfigureAwait(false);

        Assert.Contains("error", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MCP service not configured", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/* DELETE */ SELECT 1")]
    [InlineData("SELECT \"DELETE\" FROM x")]
    [InlineData("SELECT TOP 10 * FROM dbo.PRSummaryMain")]
    [InlineData("SELECT col1 AS into_alias FROM dbo.X")]
    [InlineData("SELECT 1 AS OPENROWSETXYZ")]
    public async Task QueryKorDataAsync_AllowsReadOnlyStatementsThroughGate(string sql)
    {
        var result = await ExecuteAsync(sql).ConfigureAwait(false);

        Assert.Contains("MCP service not configured", result, StringComparison.OrdinalIgnoreCase);
    }

    private static Task<string> ExecuteAsync(string sql)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new McpOptions());
        var audit = new AuditLogger(options, NullLogger<AuditLogger>.Instance);
        var tool = new QueryKorDataTool(options, audit, NullLogger<QueryKorDataTool>.Instance);
        return tool.QueryKorDataAsync(sql, CancellationToken.None);
    }
}
