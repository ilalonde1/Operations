// ArcGisFingerprint — walk ArcGIS servers and say which layers are development
// APPLICATIONS, judged on their schema rather than their name.
//
// Why this exists. Finding these by name does not work. On 2026-09-10 a keyword
// sweep of the ArcGIS Online catalogue returned ten hits for Kelowna and every
// one was a "Development Permit Area" — a zoning overlay. Surrey publishes an
// applications table, four overlays and an issued-permit table on one server,
// and every one of them matches the same keywords. A schema cannot pretend the
// same way: an overlay has no file number, because an overlay is not a case.
//
//   ArcGisFingerprint https://maps.kamloops.ca/arcgis/rest/services
//   ArcGisFingerprint --org https://services5.arcgis.com/YRpe0VKTJytZSSIB
//   ArcGisFingerprint --file roots.txt --counts
//
// --counts also asks each candidate layer how many rows it has, which turns a
// schema match into something worth acting on. Reads only; writes nothing.
using System.Globalization;
using System.Text.Json;
using Kor.Opportunities.Data.Ingestion.Discovery;

var roots = new List<string>();
var withCounts = false;
var showAll = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--counts":
            withCounts = true;
            break;
        case "--all":
            showAll = true;
            break;
        case "--org" when i + 1 < args.Length:
            roots.Add(args[++i].TrimEnd('/') + "/arcgis/rest/services");
            break;
        case "--file" when i + 1 < args.Length:
            foreach (var line in File.ReadAllLines(args[++i]))
            {
                var t = line.Trim();
                if (t.Length > 0 && !t.StartsWith('#'))
                {
                    roots.Add(t.TrimEnd('/'));
                }
            }

            break;
        default:
            if (args[i].StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(args[i].TrimEnd('/'));
            }
            else
            {
                Console.Error.WriteLine($"Unknown argument: {args[i]}");
                return 2;
            }

            break;
    }
}

if (roots.Count == 0)
{
    Console.Error.WriteLine(
        "Usage: ArcGisFingerprint <serverRootUrl> [more...] [--org <base>] [--file <list>] [--counts] [--all]");
    return 2;
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
http.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Kor.ArcGisFingerprint/1.0 (+ilalonde@korstructural.com)");

var totalLayers = 0;
var verdicts = new List<(string Root, string Url, LayerVerdict V, long? Rows)>();

foreach (var root in roots)
{
    Console.WriteLine($"== {root}");
    var services = new List<(string Name, string Type)>();

    var rootDoc = await GetAsync(root);
    if (rootDoc is null)
    {
        Console.WriteLine("   unreachable");
        Console.WriteLine();
        continue;
    }

    AddServices(rootDoc.Value, services);

    var folders = new List<string>();
    if (rootDoc.Value.TryGetProperty("folders", out var f) && f.ValueKind == JsonValueKind.Array)
    {
        folders.AddRange(f.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
    }

    foreach (var folder in folders)
    {
        var doc = await GetAsync($"{root}/{folder}");
        if (doc is not null)
        {
            AddServices(doc.Value, services);
        }
    }

    Console.WriteLine($"   {folders.Count} folder(s), {services.Count} service(s)");

    foreach (var (svcName, svcType) in services)
    {
        if (svcType is not ("MapServer" or "FeatureServer"))
        {
            continue;
        }

        var svcUrl = $"{root}/{svcName}/{svcType}";
        var svcDoc = await GetAsync(svcUrl);
        if (svcDoc is null || !svcDoc.Value.TryGetProperty("layers", out var layers)
            || layers.ValueKind != JsonValueKind.Array)
        {
            continue;
        }

        foreach (var layer in layers.EnumerateArray())
        {
            if (!layer.TryGetProperty("id", out var idEl))
            {
                continue;
            }

            var id = idEl.GetInt32();
            var layerName = layer.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";

            // Cheap pre-filter: a layer with none of these words in its name is
            // not worth a schema fetch. Deliberately WIDE — it only has to avoid
            // fetching every contour and hydrant, and the schema decides.
            if (!Interesting(layerName))
            {
                continue;
            }

            var layerUrl = $"{svcUrl}/{id}";
            var layerDoc = await GetAsync(layerUrl);
            if (layerDoc is null)
            {
                continue;
            }

            var fields = new List<string>();
            if (layerDoc.Value.TryGetProperty("fields", out var fs) && fs.ValueKind == JsonValueKind.Array)
            {
                fields.AddRange(fs.EnumerateArray()
                    .Select(x => x.TryGetProperty("name", out var fn) ? fn.GetString() ?? "" : "")
                    .Where(x => x.Length > 0));
            }

            totalLayers++;
            var verdict = ApplicationLayerFingerprint.Classify(layerName, fields);

            long? rows = null;
            if (withCounts && verdict.Kind is LayerKind.Applications or LayerKind.IssuedPermits)
            {
                var cnt = await GetAsync($"{layerUrl}/query?where=1%3D1&returnCountOnly=true&f=json");
                if (cnt is not null && cnt.Value.TryGetProperty("count", out var c)
                    && c.ValueKind == JsonValueKind.Number)
                {
                    rows = c.GetInt64();
                }
            }

            verdicts.Add((root, layerUrl, verdict, rows));
        }
    }

    Console.WriteLine();
}

Console.WriteLine(new string('-', 100));
Console.WriteLine($"{totalLayers} layer(s) fingerprinted across {roots.Count} root(s)");
foreach (var kind in new[] { LayerKind.Applications, LayerKind.IssuedPermits, LayerKind.ZoningOverlay, LayerKind.NotApplications })
{
    Console.WriteLine($"   {kind,-18} {verdicts.Count(v => v.V.Kind == kind)}");
}

Console.WriteLine();
foreach (var kind in showAll
             ? new[] { LayerKind.Applications, LayerKind.IssuedPermits, LayerKind.ZoningOverlay, LayerKind.NotApplications }
             : new[] { LayerKind.Applications, LayerKind.IssuedPermits })
{
    var rows = verdicts.Where(v => v.V.Kind == kind).ToList();
    if (rows.Count == 0)
    {
        continue;
    }

    Console.WriteLine($"### {kind} ({rows.Count})");
    foreach (var (_, url, v, n) in rows.OrderByDescending(r => r.Rows ?? -1))
    {
        var count = n is null ? "" : $"  [{n.Value.ToString("N0", CultureInfo.InvariantCulture)} rows]";
        Console.WriteLine($"  {v.LayerName}{count}");
        Console.WriteLine($"     {url}");
        Console.WriteLine($"     why: {v.Why}");
        if (v.SuggestedMapping.Count > 0)
        {
            foreach (var kv in v.SuggestedMapping.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"     ({kv.Key,-28} {kv.Value})");
            }
        }

        Console.WriteLine();
    }
}

return 0;

static bool Interesting(string name)
{
    var n = name.ToLowerInvariant();
    return n.Contains("develop") || n.Contains("permit") || n.Contains("applic")
           || n.Contains("rezon") || n.Contains("planning") || n.Contains("subdiv")
           || n.Contains("project") || n.Contains("zoning");
}

static void AddServices(JsonElement doc, List<(string, string)> into)
{
    if (!doc.TryGetProperty("services", out var svcs) || svcs.ValueKind != JsonValueKind.Array)
    {
        return;
    }

    foreach (var s in svcs.EnumerateArray())
    {
        var name = s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var type = s.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        if (name.Length > 0 && type.Length > 0)
        {
            into.Add((name, type));
        }
    }
}

async Task<JsonElement?> GetAsync(string url)
{
    var sep = url.Contains('?') ? "&" : "?";
    var full = url.Contains("f=json") ? url : url + sep + "f=json";
    try
    {
        using var resp = await http.GetAsync(full);
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        // ArcGIS answers 200 with an error body. Treat that as a failure.
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("error", out _))
        {
            return null;
        }

        return doc.RootElement.Clone();
    }
    catch
    {
        return null;
    }
}
