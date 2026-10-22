#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment;

/// <summary>
/// How a staff identifier reads in a grid column. Owners are stored as UPNs ("ishabana@korstructural.com") or bare
/// logins and first names; the screens showed "ishabana" and "cmurtagh@" where a person's name belongs (Ian,
/// 2026-10-21). Names come from the BD staff directory (opportunities.BdStaff), loaded once per session; until it has
/// loaded, or for a key it does not know, the part before the @ is shown.
/// </summary>
internal static class StaffDisplay
{
    private static IReadOnlyDictionary<string, string> _names = new Dictionary<string, string>();
    private static int _loaded;

    internal static async Task EnsureLoadedAsync(IBdStaffDirectory directory, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _loaded, 1) == 1) return;
        try
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in await directory.GetAllAsync(ct).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(row.DisplayName)) continue;
                names.TryAdd(row.StaffKey.Trim(), row.DisplayName.Trim());
                if (!string.IsNullOrWhiteSpace(row.Mailbox)) names.TryAdd(row.Mailbox.Trim(), row.DisplayName.Trim());
            }
            _names = names;
        }
        catch
        {
            // Logins stay on screen; names are a nicety, never a reason for a screen to fail. Retried next window.
            Interlocked.Exchange(ref _loaded, 0);
        }
    }

    internal static string Short(string? staffId)
    {
        if (string.IsNullOrWhiteSpace(staffId))
        {
            return "";
        }

        var id = staffId.Trim();
        if (_names.TryGetValue(id, out var name)) return name;
        var at = id.IndexOf('@');
        var login = at > 0 ? id[..at] : id;
        return _names.TryGetValue(login, out name) ? name : login;
    }
}
