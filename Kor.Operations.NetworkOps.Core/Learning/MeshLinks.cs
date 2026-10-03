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

    /// <summary>
    /// The app's own remote-control window (KorRemoteViewerWindow): the desktop page with MeshCentral's header (1), top bar
    /// (2), footer (4) and panel titles (8) hidden -- MeshCentral 1.2.5 default.handlebars, adjustPanels(). 16 (the left bar)
    /// is commented out in 1.2.5; the page's own full-screen mode hides that, and the bridge turns it on.
    /// </summary>
    public const int ViewerHide = 1 | 2 | 4 | 8;

    /// <summary>The page is a device link (signing in lands on MeshCentral's home page instead; the viewer sends it back).</summary>
    public static bool IsDeviceLink(Uri? page) => page?.Query.Contains("gotonode=", StringComparison.Ordinal) == true;

    /// <summary>A device page for the app's own window with MeshCentral's chrome hidden: the Desktop tab (viewMode 11, the
    /// default) for Connect, or the general page (viewMode 10) for Web-RDP -- both in the same clean window, no browser.</summary>
    public static string? ViewerUrl(string? baseUrl, string? meshNodeId, int viewMode = 11)
        => DeviceUrl(baseUrl, meshNodeId, viewMode) is { } url ? $"{url}&hide={ViewerHide}" : null;
}
