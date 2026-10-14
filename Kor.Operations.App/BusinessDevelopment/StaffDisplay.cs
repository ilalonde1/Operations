#nullable enable
using System;

namespace Kor.Operations.App.BusinessDevelopment;

/// <summary>
/// How a staff identifier reads in a grid column. Owners are stored as UPNs
/// ("ilalonde@korstructural.com"); a 90 to 110 px column showed "ilalonde@korstru" on
/// every pursuit row. The part before the @ is the person; the domain is always KOR's.
/// </summary>
internal static class StaffDisplay
{
    internal static string Short(string? staffId)
    {
        if (string.IsNullOrWhiteSpace(staffId))
        {
            return "";
        }

        var at = staffId.IndexOf('@');
        return at > 0 ? staffId[..at] : staffId.Trim();
    }
}
