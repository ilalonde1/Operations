#nullable enable
using System;
using System.Data.Odbc;

namespace Kor.Operations.Tests.Financials.Executive.Integration;

/// <summary>
/// Opens a real ODBC connection to Deltek for integration tests. Reads
/// credentials from environment variables so the test project never has
/// secrets checked in. Tests using this fixture are tagged
/// <c>[Trait("Category", "Integration")]</c> and excluded from the
/// hermetic CI run.
///
/// Required env vars: DELTEK_DSN, DELTEK_USER, DELTEK_PASSWORD.
/// Optional env var: DELTEK_CATALOG (for ExecutiveSummaryLoaderSupport.Catalog).
/// </summary>
internal static class IntegrationFixture
{
    public static OdbcConnection OpenDeltekConnection()
    {
        var dsn = Environment.GetEnvironmentVariable("DELTEK_DSN");
        var user = Environment.GetEnvironmentVariable("DELTEK_USER");
        var pwd = Environment.GetEnvironmentVariable("DELTEK_PASSWORD");
        if (string.IsNullOrWhiteSpace(dsn) ||
            string.IsNullOrWhiteSpace(user) ||
            string.IsNullOrWhiteSpace(pwd))
        {
            throw new InvalidOperationException(
                "Integration tests require DELTEK_DSN, DELTEK_USER, DELTEK_PASSWORD environment variables. " +
                "Set them locally to run; integration tests are excluded from CI.");
        }

        var connStr = $"DSN={dsn};UID={user};PWD={pwd};";
        var cn = new OdbcConnection(connStr);
        cn.Open();
        return cn;
    }
}
