#nullable enable
using System.Globalization;

namespace Kor.Operations.NetworkOps.Core.Health;

/// <summary>A calendar day as the rules' evidence and the PC page's rows write it ("2026-10-16"). One place, so the
/// language ratchet (NetworkOpsLanguageTests.Dates_are_formatted_in_one_place) only ever comes down; the callers convert
/// to local time first where the value is UTC.</summary>
public static class Dates
{
    public static string Day(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A clock time ("23:12", or "23:12:05" for a status line's "updated at"); the caller converts UTC to local first.</summary>
    public static string Clock(DateTime d, bool seconds = false) => d.ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
}
