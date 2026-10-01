#nullable enable
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kor.Operations.NetworkOps.Transport;

/// <param name="Id">MeshCentral's node id, e.g. "node//bJ@yhUB..." (what a Connect link names).</param>
/// <param name="MeshId">The device group it is in, e.g. "mesh//5Jy@ap0...".</param>
/// <param name="Conn">MeshCentral's connection flags: 1 = agent connected, 2 = CIRA, 4 = Intel AMT, 8 = relay.</param>
public sealed record MeshNode(string Id, string Name, string MeshId, int Conn)
{
    /// <summary>The agent is connected. A FLAG, not a value: 5 (agent + AMT) is connected too (09-30: counting only 1 misread 12 PCs as down).</summary>
    public bool AgentConnected => (Conn & 1) != 0;
}

// Reads KOR-MESH01's device list, read-only, as the "networkops" MeshCentral account -- which has membership of the
// two device groups and no device rights at all (no remote control, terminal, files): listing is all NetworkOps needs.
// The protocol is MeshCentral's own control channel, as its meshctrl tool uses it: a websocket to /control.ashx with
// an x-meshauth header (base64 user, base64 password), then {"action":"nodes"}. MESH01's certificate is self-signed and
// trusted by its SHA-256 alone.
public sealed class MeshCentralClient(Uri baseUrl, string certSha256, string user, string password)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<MeshNode>> ListNodesAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);
        using var ws = new ClientWebSocket();
        var pin = certSha256.Replace(":", "").Replace(" ", "").ToUpperInvariant();
        ws.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())) == pin;
        ws.Options.SetRequestHeader("x-meshauth", B64(user) + "," + B64(password));
        var url = new UriBuilder(baseUrl) { Scheme = "wss", Path = "/control.ashx" }.Uri;
        await ws.ConnectAsync(url, deadline.Token).ConfigureAwait(false);
        await SendAsync(ws, """{"action":"nodes","responseid":"networkops"}""", deadline.Token).ConfigureAwait(false);

        while (true)
        {
            var text = await ReceiveAsync(ws, deadline.Token).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("MeshCentral closed the connection before sending the device list");
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var action = root.TryGetProperty("action", out var a) ? a.GetString() : null;
            if (action == "close") throw new UnauthorizedAccessException("MeshCentral refused the login: " + (root.TryGetProperty("cause", out var c) ? c.GetString() : "no reason given"));
            if (action != "nodes") continue;   // serverinfo, userinfo... arrive first
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false); } catch (WebSocketException) { }
            return Parse(root);
        }
    }

    /// <summary>The "nodes" answer: { "nodes": { "mesh//...": [ { "_id": "node//...", "name": "...", "conn": 1 }, ... ] } }.</summary>
    internal static IReadOnlyList<MeshNode> Parse(JsonElement root)
    {
        var list = new List<MeshNode>();
        if (!root.TryGetProperty("nodes", out var groups) || groups.ValueKind != JsonValueKind.Object) return list;
        foreach (var group in groups.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var n in group.Value.EnumerateArray())
            {
                var id = n.TryGetProperty("_id", out var i) ? i.GetString() : null;
                var name = n.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                if (id is null || name is null) continue;
                var conn = n.TryGetProperty("conn", out var cn) && cn.TryGetInt32(out var v) ? v : 0;
                list.Add(new MeshNode(id, name, group.Name, conn));
            }
        }
        return list;
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    private static Task SendAsync(ClientWebSocket ws, string text, CancellationToken ct)
        => ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);

    /// <summary>One whole text message (the device list arrives in many frames), or null when the server closed.</summary>
    private static async Task<string?> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, r.Count);
            if (ms.Length > 32 * 1024 * 1024) throw new InvalidOperationException("MeshCentral message over 32 MB");
            if (r.EndOfMessage) return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    /// <summary>The part of a node id a Connect link carries: "node//abc" -> "abc".</summary>
    public static string LinkId(string nodeId) => nodeId.StartsWith("node//", StringComparison.Ordinal) ? nodeId["node//".Length..] : nodeId;
}
