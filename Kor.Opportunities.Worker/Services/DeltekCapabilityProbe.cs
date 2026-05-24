#nullable enable
using System;
using System.Data.Odbc;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Worker.Services;

internal static class DeltekCapabilityProbe
{
    public static bool TryPing(ILogger logger, out string detail)
    {
        try
        {
            using var con = new OdbcConnection("DSN=Deltek;");
            con.Open();
            using var cmd = new OdbcCommand("SELECT 1", con);
            cmd.CommandTimeout = 5;
            var result = cmd.ExecuteScalar();
            detail = $"DSN=Deltek ping ok (driver={con.Driver})";
            return result != null;
        }
        catch (Exception ex)
        {
            detail = $"DSN=Deltek ping failed: {ex.GetType().Name}: {ex.Message}";
            logger.LogDebug(ex, "Deltek capability probe failed.");
            return false;
        }
    }
}
