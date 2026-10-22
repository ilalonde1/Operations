#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
            _names = BuildNames(await directory.GetAllAsync(ct).ConfigureAwait(false));
        }
        catch
        {
            // Logins stay on screen; names are a nicety, never a reason for a screen to fail.
            lock (Gate) _loading = null;
        }
    }

    /// <summary>The key → name map, from the directory rows. Pure, so it is tested without the session cache.</summary>
    internal static Dictionary<string, string> BuildNames(IEnumerable<BdStaffRow> directoryRows)
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
        // A row's own key outranks another row's mailbox: "Jim" (mailbox jdesroches@…) and "jdesroches@…" (James
        // DesRoches) both claimed jdesroches@…, the clash dropped it, and All Pursuits showed Jim as "jdesroches"
        // (2026-10-22). So keys go in first, and a mailbox only names a key no row owns.
        var rows = directoryRows.Where(r => !string.IsNullOrWhiteSpace(r.DisplayName)).ToList();
        foreach (var row in rows) Add(row.StaffKey.Trim(), row.DisplayName!.Trim());
        var ownedKeys = new HashSet<string>(rows.Select(r => r.StaffKey.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.Mailbox) && !ownedKeys.Contains(row.Mailbox.Trim()))
                Add(row.Mailbox.Trim(), row.DisplayName!.Trim());
        }
        foreach (var key in ambiguous) names.Remove(key);
        return names;
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
