#nullable enable
using System;
using System.Data.Odbc;
using Kor.Operations.App.Options;
using Kor.Operations.Financials;

namespace Kor.Operations.Tests.Financials.Executive.Integration;

/// <summary>
/// Opens a real ODBC connection to Deltek for integration tests. Reads
/// credentials from environment variables so the test project never has
/// secrets checked in. Tests using this fixture are tagged
/// <c>[Trait("Category", "Integration")]</c> and excluded from the
/// hermetic CI run.
///
/// Credentials resolve in this order:
/// <list type="number">
/// <item>DELTEK_DSN / DELTEK_USER / DELTEK_PASSWORD — explicit overrides.</item>
/// <item>KOR_ODBC_USER / KOR_ODBC_PASSWORD with DSN <c>Deltek</c> — the
/// machine-level names the app itself uses (see App.xaml.cs), so a normally
/// provisioned workstation runs these tests without extra setup.</item>
/// </list>
/// </summary>
internal static class IntegrationFixture
{
    /// <summary>Default system DSN name on a provisioned KOR workstation.</summary>
    private const string DefaultDsn = "Deltek";

    public static OdbcConnection OpenDeltekConnection()
    {
        var (dsn, user, pwd) = ResolveCredentials();
        var cn = new OdbcConnection($"DSN={dsn};UID={user};PWD={pwd};");
        cn.Open();
        return cn;
    }

    /// <summary>
    /// Options carrying the same resolved credentials, for services that open
    /// their own connection rather than taking one.
    /// </summary>
    public static DeltekOdbcOptions Options()
    {
        var (dsn, user, pwd) = ResolveCredentials();
        return new DeltekOdbcOptions
        {
            Dsn = dsn,
            User = user,
            Password = pwd,
            Catalog = ExecutiveSummaryLoaderSupport.Catalog,
        };
    }

    private static (string Dsn, string User, string Password) ResolveCredentials()
    {
        var dsn = FirstNonBlank("DELTEK_DSN") ?? DefaultDsn;
        var user = FirstNonBlank("DELTEK_USER", "KOR_ODBC_USER");
        var pwd = FirstNonBlank("DELTEK_PASSWORD", "KOR_ODBC_PASSWORD");
        if (user is null || pwd is null)
        {
            throw new InvalidOperationException(
                "Integration tests require Deltek ODBC credentials. Set DELTEK_USER/DELTEK_PASSWORD " +
                "(and optionally DELTEK_DSN), or the machine-level KOR_ODBC_USER/KOR_ODBC_PASSWORD the " +
                "app already uses. Integration tests are excluded from CI via Category=Integration.");
        }

        return (dsn, user, pwd);
    }

    private static string? FirstNonBlank(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
