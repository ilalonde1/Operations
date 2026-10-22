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
    private static readonly object Gate = new();
    private static Task? _loading;

    // Every window awaits the same load: a second window opened while the first was loading used to go ahead with logins
    // (audit m1, 2026-10-21). A failed load is forgotten so the next window tries again.
    internal static Task EnsureLoadedAsync(IBdStaffDirectory directory, CancellationToken ct)
    {
        lock (Gate)
        {
            return _loading ??= LoadAsync(directory, ct);
        }
    }

    private static async Task LoadAsync(IBdStaffDirectory directory, CancellationToken ct)
    {
        try
        {
            // A key that two directory rows give different names (a bare "John") names nobody: it is left out rather than
            // resolved by whichever row came back first (m1). Full UPNs never collide.
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string key, string name)
            {
                if (names.TryGetValue(key, out var had) && !string.Equals(had, name, StringComparison.OrdinalIgnoreCase)) ambiguous.Add(key);
                else names[key] = name;
            }
            foreach (var row in await directory.GetAllAsync(ct).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(row.DisplayName)) continue;
                Add(row.StaffKey.Trim(), row.DisplayName.Trim());
                if (!string.IsNullOrWhiteSpace(row.Mailbox)) Add(row.Mailbox.Trim(), row.DisplayName.Trim());
            }
            foreach (var key in ambiguous) names.Remove(key);
            _names = names;
        }
        catch
        {
            // Logins stay on screen; names are a nicety, never a reason for a screen to fail.
            lock (Gate) _loading = null;
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
