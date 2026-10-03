using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kor.Operations.App.NetworkOps;

/// <summary>
/// The window's side of KorRemoteBridge.js: the script itself, the page functions it depends on, and what it reports.
/// The script runs in MeshCentral's page and calls only the page's own functions (MeshCentral 1.2.5 default.handlebars);
/// <see cref="RequiredFunctions"/> is the same list the script checks, kept equal by a test, so a MeshCentral update that
/// renames one is reported by name in the window instead of leaving a button that does nothing.
/// </summary>
public static class KorRemoteBridge
{
    public static readonly IReadOnlyList<string> RequiredFunctions =
        ["connectDesktop", "sendCAD", "deskSaveImage", "deskToggleFull", "deskSendKeys", "deskSetDisplay", "deviceChat", "deviceLockFunction", "go"];

    private static readonly Lazy<string> _script = new(() =>
    {
        using var s = typeof(KorRemoteBridge).Assembly.GetManifestResourceStream("KorRemoteBridge.js")
                      ?? throw new InvalidOperationException("KorRemoteBridge.js is not embedded in the app");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    });

    public static string Script => _script.Value;

    /// <summary>A toolbar command as the script expression that carries it out (window.__korRemote.*).</summary>
    public static string Command(string name, string? argument = null)
        => argument is null ? $"window.__korRemote && window.__korRemote.{name}();" : $"window.__korRemote && window.__korRemote.{name}({JsonSerializer.Serialize(argument)});";

    public static string Command(string name, int argument) => $"window.__korRemote && window.__korRemote.{name}({argument});";

    public static string Command(string name, bool argument) => $"window.__korRemote && window.__korRemote.{name}({(argument ? "true" : "false")});";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>What the script posted (WebMessageAsJson), or null when it is not the script's message.</summary>
    public static BridgeState? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<BridgeState>(json, Json); }
        catch (JsonException) { return null; }
    }
}

/// <summary>The page as the bridge sees it.</summary>
/// <param name="Page">"desktop" (MeshCentral's device page), "login" (its sign-in), or "other".</param>
/// <param name="State">MeshCentral's desktop.State: 0 disconnected, 1 connecting, 2 setting up, 3 connected.</param>
public sealed record BridgeState(
    string Page,
    IReadOnlyList<string>? Missing = null,
    string? Node = null,
    int State = 0,
    IReadOnlyList<BridgeKey>? Keys = null,
    IReadOnlyList<BridgeDisplay>? Displays = null,
    bool Files = false);

/// <summary>A key combination from MeshCentral's own list (#deskkeys): its value is what deskSendKeys sends.</summary>
public sealed record BridgeKey(string Value, string Text);

public sealed record BridgeDisplay(int Number, string Name, bool Selected);
