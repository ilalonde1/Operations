#nullable enable
using System;
using System.Globalization;

namespace Kor.Operations.App.BusinessDevelopment;

/// <summary>Non-deadline dates on BD screens, in the reader's local time and one invariant format.</summary>
internal static class BdDateDisplay
{
    internal static string Day(DateTimeOffset value)
        => value.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    internal static string Day(DateTime value)
        => (value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value)
            .ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    internal static string DayAndTime(DateTimeOffset value)
        => value.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
}
