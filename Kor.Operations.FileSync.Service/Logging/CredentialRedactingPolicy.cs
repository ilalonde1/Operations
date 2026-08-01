#nullable enable
using Serilog.Core;
using Serilog.Events;

namespace Kor.Operations.FileSync.Service.Logging;

internal sealed class CredentialRedactingPolicy : IDestructuringPolicy
{
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory factory,
        out LogEventPropertyValue result)
    {
        if (value is string str && CredentialPatterns.CredentialPattern.IsMatch(str))
        {
            result = factory.CreatePropertyValue(
                CredentialPatterns.CredentialPattern.Replace(str, "$1=***REDACTED***"));
            return true;
        }

        result = null!;
        return false;
    }
}
