#nullable enable
using Serilog.Core;
using Serilog.Events;

namespace Kor.Operations.Logging;

internal sealed class CredentialRedactingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Exception is not null)
        {
            var cleanMessage = CredentialPatterns.CredentialPattern.Replace(
                logEvent.Exception.Message, "$1=***REDACTED***");

            logEvent.AddOrUpdateProperty(
                propertyFactory.CreateProperty(
                    "SanitizedExceptionMessage", cleanMessage));
        }
    }
}
