#nullable enable
using System.Text.RegularExpressions;

namespace Kor.Operations.NetworkOps.Core.Learning;

/// <summary>
/// The ONE way to link to a device in KOR Remote (MeshCentral): the app's Connect / Web-RDP and the Ask Claude prompts.
///
/// MeshCentral reads its URL arguments WITHOUT decoding them (public/scripts/common-0.0.1.js, parseUriArgs() called with no
/// decode flag), so the node id must go in exactly as MeshCentral writes it in its own links: raw, '@' and '$' included.
/// Both callers escaped it (Uri.EscapeDataString: '@' -> '%40'), MeshCentral then looked for a node named "...%40...",
/// found none, and opened an empty Desktop panel -- Ian, 2026-10-02: "the link from the app doesn't [work]", on KOR-101,
/// whose id has two '@'. A node id is MeshCentral's base64 with '+' -> '@' and '/' -> '$'; anything else gives no link.
/// </summary>
public static class MeshLinks
{
    /// <summary>The desktop (viewMode 11) or general (10) page of a device, or null when there is no usable id.</summary>
    public static string? DeviceUrl(string? baseUrl, string? meshNodeId, int viewMode = 11)
    {
        if (baseUrl is not { Length: > 0 } || meshNodeId is not { Length: > 0 }) return null;
        var id = meshNodeId.StartsWith("node//", StringComparison.Ordinal) ? meshNodeId["node//".Length..] : meshNodeId;
        if (!Regex.IsMatch(id, "^[A-Za-z0-9@$]+$")) return null;   // not a MeshCentral id: no link beats a broken one
        return $"{baseUrl.TrimEnd('/')}/?gotonode={id}&viewmode={viewMode}";
    }
}
