using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Kor.Operations.Core;

namespace Kor.Operations.App.NetworkOps;

/// <summary>
/// The KOR Remote viewer's state, from what the bridge reports from MeshCentral's page: the light and words in the title
/// bar, which toolbar controls work, the monitors and key combinations offered. No WebView2 here, so it is tested on its own.
/// </summary>
public sealed class KorRemoteViewerModel : ObservableObject
{
    private BridgeState? _page;

    public KorRemoteViewerModel(string deviceName, string? presence)
    {
        DeviceName = deviceName;
        Presence = presence;
    }

    public string DeviceName { get; }
    /// <summary>Who was on it at the last check ("kwurmlinger · active, idle 12 min"), or null.</summary>
    public string? Presence { get; private set; }
    public string Title => $"{DeviceName} — KOR Remote";

    public void SetPresence(string? presence) { Presence = presence; OnPropertyChanged(nameof(Presence)); OnPropertyChanged(nameof(PresenceText)); }

    public string PresenceText => Presence is { Length: > 0 } p ? $"At the last check: {p}" : "Nobody was signed in at the last check, or it has no agent.";

    /// <summary>A report from the bridge. A message that is not one (null) changes nothing.</summary>
    public void Apply(BridgeState? state)
    {
        if (state is null) return;
        _page = state;
        foreach (var name in new[] { nameof(IsReady), nameof(IsConnected), nameof(StatusText), nameof(Light), nameof(Problem), nameof(HasProblem),
                     nameof(Displays), nameof(ShowsDisplays), nameof(Monitors), nameof(Keys), nameof(FilesShowing), nameof(Overlay), nameof(ShowsOverlay) })
            OnPropertyChanged(name);
    }

    /// <summary>The page is MeshCentral's device page, on this device, with every function the toolbar uses.</summary>
    public bool IsReady => _page is { Page: "desktop", Node: { Length: > 0 } } p && (p.Missing?.Count ?? 0) == 0;
    public bool IsConnected => IsReady && _page!.State == 3;
    public bool FilesShowing => IsReady && _page!.Files;

    public string StatusText => _page switch
    {
        null => "Opening…",
        { Page: "login" } => "Sign in to KOR Remote",
        { Page: "desktop" } when HasProblem => "Toolbar off",
        { Page: "desktop", Node: null or "" } => "Finding the device…",
        { Page: "desktop" } p when p.Files => "Files",
        { Page: "desktop", State: 3 } => "Connected",
        { Page: "desktop", State: 1 } => "Connecting…",
        { Page: "desktop", State: 2 } => "Setting up…",
        { Page: "desktop" } => "Not connected",
        _ => "",
    };

    public Brush Light => IsConnected ? NetworkOpsBrushes.Healthy
        : _page is { Page: "desktop", State: 1 or 2 } ? NetworkOpsBrushes.Attention
        : HasProblem ? NetworkOpsBrushes.Critical : NetworkOpsBrushes.Unknown;

    /// <summary>A MeshCentral update renamed or removed a function the toolbar calls: said by name, toolbar off.</summary>
    public string Problem => _page is { Page: "desktop", Missing: { Count: > 0 } missing }
        ? $"KOR Remote's page has changed: {string.Join(", ", missing)} not found. The toolbar is off so no button pretends to work; More ▾ › Open in a browser window still connects."
        : "";
    public bool HasProblem => Problem.Length > 0;

    /// <summary>Over the page only while there is nothing on it to see; the sign-in page must stay visible.</summary>
    public string Overlay => _page is null ? "Opening KOR Remote…" : HasProblem ? Problem : "";
    public bool ShowsOverlay => Overlay.Length > 0;

    public IReadOnlyList<BridgeDisplay> Displays => IsReady ? _page!.Displays ?? [] : [];
    /// <summary>Monitor choice only where there is a choice (MeshCentral shows none for one screen).</summary>
    public bool ShowsDisplays => Displays.Count > 1;

    /// <summary>The monitor buttons in the title strip: "1", "2", ... and "All" (MeshCentral's "All Displays"), the one on
    /// screen filled. Ian, 2026-10-02: "will need to be able to switch to individual monitors or see all".</summary>
    public IReadOnlyList<MonitorChoice> Monitors => Displays
        .OrderBy(d => IsAll(d) ? 1 : 0).ThenBy(d => d.Number)
        .Select(d => new MonitorChoice(d.Number, IsAll(d) ? "All" : ShortName(d), IsAll(d) ? "Every monitor at once" : $"Only {d.Name}", d.Selected))
        .ToList();

    private static bool IsAll(BridgeDisplay d) => d.Name.Contains("All", System.StringComparison.OrdinalIgnoreCase);

    // "Display 2" -> "2"; any other name as MeshCentral gives it.
    private static string ShortName(BridgeDisplay d) => d.Name.StartsWith("Display ", System.StringComparison.OrdinalIgnoreCase) ? d.Name["Display ".Length..] : d.Name;

    /// <summary>MeshCentral's own key combinations, minus the empty first entry and Ctrl+Alt+Del (its own button).</summary>
    public IReadOnlyList<BridgeKey> Keys => IsReady
        ? (_page!.Keys ?? []).Where(k => k.Text.Length > 0 && k.Value != CtrlAltDel && !k.Text.Contains("Ctrl-Alt-Del", System.StringComparison.OrdinalIgnoreCase)).ToList()
        : [];

    // 0x0A002E: the value MeshCentral 1.2.5's deskSendKeys treats as Ctrl+Alt+Del.
    private const string CtrlAltDel = "655406";
}

/// <summary>One monitor button: what deskSetDisplay is given, its label, and whether it is the one on screen.</summary>
public sealed record MonitorChoice(int Number, string Label, string Tip, bool Selected)
{
    private static readonly Brush On = Frozen(0x5B, 0x7A, 0x99);
    private static readonly Brush Off = Frozen(0x2B, 0x33, 0x3C);
    public Brush Fill => Selected ? On : Off;
    private static Brush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
}
