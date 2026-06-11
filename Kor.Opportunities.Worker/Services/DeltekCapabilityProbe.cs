#nullable enable
using System;
using System.Data.Odbc;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Worker.Services;

internal static class DeltekCapabilityProbe
{
    public static bool TryPing(string? dsn, string? user, string? password, ILogger logger, out string detail)
    {
        // The DataDirect Hybrid driver needs DSN + UID + PWD; the System DSN on
        // KOR-APP01 carries no embedded LogonID. A bare "DSN=Deltek;" open fails
        // with "Insufficient information to connect", which nulled the Deltek
        // accessors on every startup. Mirror VpOdbcDsnFactory's string exactly.
        var resolvedDsn = string.IsNullOrWhiteSpace(dsn) ? "Deltek" : dsn;
        try
        {
            var builder = new OdbcConnectionStringBuilder
            {
                ["DSN"] = resolvedDsn,
                ["UID"] = user ?? string.Empty,
                ["PWD"] = password ?? string.Empty,
            };
            using var con = new OdbcConnection(builder.ConnectionString);
            con.Open();
            using var cmd = new OdbcCommand("SELECT 1", con);
            cmd.CommandTimeout = 5;
            var result = cmd.ExecuteScalar();
            detail = $"DSN={resolvedDsn} ping ok (driver={con.Driver})";
            return result != null;
        }
        catch (Exception ex)
        {
            detail = $"DSN={resolvedDsn} ping failed: {ex.GetType().Name}: {ex.Message}";
            logger.LogDebug(ex, "Deltek capability probe failed.");
            return false;
        }
    }
}
