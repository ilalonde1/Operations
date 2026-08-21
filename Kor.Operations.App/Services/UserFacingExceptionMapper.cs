#nullable enable
using System;
using System.Data.Odbc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Services;

internal static class UserFacingExceptionMapper
{
    internal const string DataConnectionMessage =
        "We couldn't reach KOR's data services. Connect to the KOR VPN, then try again.";

    internal const string DeltekClientNotFoundMessage =
        "No Deltek client record was found for this organization in the current Deltek connection.";

    internal const string GenericLoadMessage =
        "We couldn't load this information. Try again; if it keeps happening, send the organization name and time to support.";

    internal static string Map(Exception ex)
    {
        if (ContainsDataConnectionException(ex))
        {
            return DataConnectionMessage;
        }

        return GenericLoadMessage;
    }

    internal static string MapAndLog(ILogger logger, Exception ex, string logMessage, params object?[] args)
    {
        logger.LogWarning(ex, logMessage, args);
        return Map(ex);
    }

    private static bool ContainsDataConnectionException(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqlException or OdbcException)
            {
                return true;
            }
        }

        return false;
    }
}
