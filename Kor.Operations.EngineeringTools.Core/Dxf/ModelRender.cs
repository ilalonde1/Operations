#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Every storey of a model on one sheet, so a person can LOOK (CLAUDE.md rule 9: the output has
/// been looked at, rendered, not counted). Ported 2026-09-11 from <c>plan_sheet.py</c> and
/// <c>render_storeys.sh</c>: walls dark red, columns green, each plate in its own colour with a
/// drawn edge — filled alike, two slabs that abut read as one self-crossing ring, and a storey's
/// three slabs were misread that way for half an hour. Counts cannot see a floor in the wrong
/// place, a slab with the whole site under it, or columns standing in open air; a picture can.
/// </summary>
/// <remarks>
/// The SVG is the artefact; the PNG is Edge's screenshot of it (headless, its own profile — a
/// running Edge swallows the call otherwise — forward-slash file URL, one retry), for the terminal
/// and the deliverable. Storeys with nothing on them are not drawn, and the sheet says how many
/// were. WHAT THIS DOES NOT DRAW: beams and braces (thin purple, as the script did), openings,
/// sections' thicknesses; and it does not judge — it shows.
/// </remarks>
public static class ModelRender
{
    private static readonly Regex Story = new(@"^\s*STORY\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly string[] Shades = ["#9ec5e8", "#f5c78a", "#a8dab5", "#d7b3e0", "#f2a7a7", "#b8c9e8"];
    private static readonly string[] Edges = ["#2b6ca3", "#b3701a", "#2d7a45", "#7a3d8f", "#a83232", "#3a4f7a"];

    /// <summary>The sheet as SVG text; null when the model has nothing to draw. <paramref name="drawn"/> says how many storeys carry something.</summary>
    public static string? Svg(string e2kPath, string title, out int drawn, int columns = 3, int cellPx = 600)
    {
        var doc = E2kDocument.Load(e2kPath);
        var order = doc.ReadStories().Select(s => s.Name).ToList();
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in doc.LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^AREA\s+""([^""]+)""\s+(\w+)\b");
            if (m.Success) kinds[m.Groups[1].Value] = m.Groups[2].Value;
        }
        foreach (string raw in doc.LinesOf("LINE CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.TrimStart(), @"^LINE\s+""([^""]+)""\s+(\w+)\b");
            if (m.Success) kinds[m.Groups[1].Value] = m.Groups[2].Value;
        }
        var points = doc.PlanPointsOfObjects();
        var storeysOf = doc.StoreysByObject();
        var on = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, storeys) in storeysOf)
            foreach (var s in storeys)
                (on.TryGetValue(s, out var list) ? list : on[s] = new List<string>()).Add(name);

        var every = points.Values.SelectMany(p => p).ToList();
        drawn = 0;
        if (every.Count == 0) return null;
        double minX = every.Min(p => p.X), maxX = every.Max(p => p.X), minY = every.Min(p => p.Y), maxY = every.Max(p => p.Y);
        double w = maxX - minX, h = maxY - minY;
        var live = order.Where(s => on.ContainsKey(s) && on[s].Count > 0).ToList();
        drawn = live.Count;
        const int pad = 10, head = 18;
        int rows = (live.Count + columns - 1) / columns;
        double sc = w > 0 && h > 0 ? Math.Min(cellPx / w, cellPx / h) : 1;

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{columns * (cellPx + pad) + pad}\" height=\"{rows * (cellPx + pad + head) + pad + 30}\" style=\"background:#fff\">\n");
        sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{pad}\" y=\"20\" font-family=\"monospace\" font-size=\"15\" font-weight=\"bold\">{Escape(title)}</text>\n");
        for (int i = 0; i < live.Count; i++)
        {
            string storey = live[i];
            double ox = pad + (i % columns) * (cellPx + pad);
            double oy = 30 + pad + (i / columns) * (cellPx + pad + head);
            var objs = on[storey];
            int nf = objs.Count(o => kinds.TryGetValue(o, out var k) && k == "FLOOR");
            int nw = objs.Count(o => kinds.TryGetValue(o, out var k) && k == "PANEL");
            int nc = objs.Count(o => kinds.TryGetValue(o, out var k) && k == "COLUMN");
            sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{ox}\" y=\"{oy - 6}\" font-family=\"monospace\" font-size=\"11\">{Escape(storey)}  {nf}f {nw}w {nc}c</text>\n");
            sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{ox}\" y=\"{oy}\" width=\"{cellPx}\" height=\"{cellPx}\" fill=\"none\" stroke=\"#ddd\"/>\n");
            (double X, double Y) Xy((double X, double Y) p) => (ox + (p.X - minX) * sc, oy + cellPx - (p.Y - minY) * sc);
            string Pts(IEnumerable<(double X, double Y)> ps) => string.Join(" ", ps.Select(Xy).Select(q => $"{q.X.ToString("F1", CultureInfo.InvariantCulture)},{q.Y.ToString("F1", CultureInfo.InvariantCulture)}"));

            int nth = 0;
            foreach (var o in objs)
                if (kinds.TryGetValue(o, out var k) && k == "FLOOR" && points.TryGetValue(o, out var ps) && ps.Count >= 3)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"<polygon points=\"{Pts(ps)}\" fill=\"{Shades[nth % Shades.Length]}\" fill-opacity=\"0.5\" stroke=\"{Edges[nth % Edges.Length]}\" stroke-width=\"1.1\"/>\n");
                    nth++;
                }
            foreach (var o in objs)
            {
                if (!kinds.TryGetValue(o, out var k) || !points.TryGetValue(o, out var ps) || ps.Count == 0) continue;
                if (k == "PANEL")
                    sb.Append(CultureInfo.InvariantCulture, $"<polyline points=\"{Pts(ps)}\" fill=\"none\" stroke=\"#c0392b\" stroke-width=\"1.4\"/>\n");
                else if (k == "COLUMN")
                {
                    var (x, y) = Xy(ps[0]);
                    sb.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{x.ToString("F1", CultureInfo.InvariantCulture)}\" cy=\"{y.ToString("F1", CultureInfo.InvariantCulture)}\" r=\"1.8\" fill=\"#1e8449\"/>\n");
                }
                else if ((k == "BEAM" || k == "BRACE") && ps.Count >= 2)
                {
                    var a = Xy(ps[0]); var b = Xy(ps[1]);
                    sb.Append(CultureInfo.InvariantCulture, $"<line x1=\"{a.X.ToString("F1", CultureInfo.InvariantCulture)}\" y1=\"{a.Y.ToString("F1", CultureInfo.InvariantCulture)}\" x2=\"{b.X.ToString("F1", CultureInfo.InvariantCulture)}\" y2=\"{b.Y.ToString("F1", CultureInfo.InvariantCulture)}\" stroke=\"#7d3c98\" stroke-width=\"0.8\"/>\n");
                }
            }
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    /// <summary>The sheet written as SVG, and as PNG through Edge when Edge is there; the paths written, and the storeys drawn.</summary>
    public static (string Svg, string? Png, int Drawn) Write(string e2kPath, string outPngOrSvg, string title, int columns = 3, int cellPx = 600, bool png = true)
    {
        string svgPath = Path.ChangeExtension(outPngOrSvg, ".svg");
        string? svg = Svg(e2kPath, title, out int drawn, columns, cellPx);
        File.WriteAllText(svgPath, svg ?? "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"400\" height=\"40\"><text x=\"10\" y=\"25\">nothing to draw</text></svg>", new UTF8Encoding(false));
        if (!png) return (svgPath, null, drawn);
        string pngPath = Path.ChangeExtension(outPngOrSvg, ".png");
        return (svgPath, Screenshot(svgPath, pngPath) ? pngPath : null, drawn);
    }

    /// <summary>Edge headless, its own profile, forward-slash URL, one retry: what render_storeys.sh learned (2026-09-10).</summary>
    public static bool Screenshot(string svgPath, string pngPath, int sizePx = 1900)
    {
        string? edge = new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        }.FirstOrDefault(File.Exists);
        if (edge is null) return false;
        string profile = Path.Combine(Path.GetTempPath(), "kor-drawings", "edge-profile");
        Directory.CreateDirectory(profile);
        string url = "file:///" + Path.GetFullPath(svgPath).Replace('\\', '/');
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (File.Exists(pngPath)) File.Delete(pngPath);
                var psi = new ProcessStartInfo(edge)
                {
                    Arguments = $"--headless=new --disable-gpu --hide-scrollbars --no-first-run --user-data-dir=\"{profile}\" --window-size={sizePx},{sizePx} --screenshot=\"{Path.GetFullPath(pngPath)}\" \"{url}\"",
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(60_000);
                // the PNG lands a moment AFTER the process exits (a child writes it): poll for it
                for (int wait = 0; wait < 20; wait++)
                {
                    if (File.Exists(pngPath) && new FileInfo(pngPath).Length > 0) return true;
                    Thread.Sleep(250);
                }
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { }
            Thread.Sleep(3000);
        }
        return false;
    }

    private static string Escape(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
