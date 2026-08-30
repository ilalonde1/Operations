// THE DRAWING HALF, IN THE CODEBASE WITH THE REST OF IT.
//
// This was 776 lines of PowerShell under tools/ while the extractor sat in a proper project — half a
// job, and the half left behind was the one that produces the actual deliverable. It also sat in the
// exact folder this tool's own findings page names as where prototypes go to die.
//
// Visio has no managed API worth the name, so every call here goes through IDispatch on `dynamic`.
// That is why the project targets net8.0-windows. The objects and the cell formulas are identical to
// what the script sent; what changes is that the layouts are now compiled, covered by tests, and
// refactorable.
//
// WHY THE MODEL AND THE DRAWING STAY APART. Extraction knows nothing about Visio and rendering knows
// nothing about Roslyn. The model is committed as text so `git diff` shows the architecture moving;
// the .vsdx is an OUTPUT and editing it is pointless, because the next run overwrites it.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Kor.Operations.Architecture;

public sealed record RenderResult(string VsdxPath, IReadOnlyList<string> PngPaths, IReadOnlyList<string> Notes);

[SupportedOSPlatform("windows")]
public static class VisioRenderer
{
    // ---- the palette -------------------------------------------------------------------------

    private const string Ink = "RGB(28,37,48)";
    private const string Hairline = "RGB(150,158,166)";
    private const string Accent = "RGB(0,90,150)";

    /// <summary>Reading order of the business, not alphabetical: what comes in off a drawing first,
    /// then the app that runs it, then the commercial side, then the shared floor underneath.</summary>
    private static readonly string[] ClusterOrder =
    {
        "drawing intake", "desktop app", "BD platform", "AI / MCP", "email + transmittals", "shared",
    };

    private static readonly Dictionary<string, string> ClusterFill = new(StringComparer.Ordinal)
    {
        ["drawing intake"] = "RGB(222,235,247)",
        ["desktop app"] = "RGB(226,240,226)",
        ["BD platform"] = "RGB(252,236,219)",
        ["AI / MCP"] = "RGB(238,230,246)",
        ["email + transmittals"] = "RGB(249,240,225)",
        ["shared"] = "RGB(238,238,238)",
        ["one-off tools"] = "RGB(244,244,244)",
    };

    private static readonly Dictionary<string, string> GraphFill = new(StringComparer.Ordinal)
    {
        ["drawing intake"] = "RGB(120,170,215)",
        ["desktop app"] = "RGB(130,190,130)",
        ["BD platform"] = "RGB(240,170,100)",
        ["AI / MCP"] = "RGB(180,150,215)",
        ["email + transmittals"] = "RGB(225,190,120)",
        ["shared"] = "RGB(170,180,190)",
        ["one-off tools"] = "RGB(215,220,225)",
        ["external"] = "RGB(250,215,90)",
        ["artefact"] = "RGB(255,244,214)",
        ["read"] = "RGB(150,195,235)",
        ["compose"] = "RGB(150,205,150)",
        ["classify"] = "RGB(215,175,235)",
        ["write"] = "RGB(245,175,120)",
    };

    /// <summary>`Kor.Operations.EngineeringTools.Core` → `EngineeringTools.Core`. The shared prefix is
    /// the least informative part of every label on the page, and fifteen characters of nothing in a
    /// heading rotated sixty degrees.</summary>
    private static readonly Regex Prefix = new(
        @"^Kor\.(?:Operations|Opportunities)\.|^Kor\.",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string Short(string n) => Prefix.Replace(n, "");

    private static readonly Regex NotWord = new(@"[^\w]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string N0(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
    private static string Inv(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    // ---- entry point -------------------------------------------------------------------------

    public static RenderResult Render(ArchModel model, string outDir, bool keepVisioOpen = false)
    {
        Directory.CreateDirectory(outDir);
        string vsdxPath = Path.Combine(outDir, "KOR-Application-Map.vsdx");
        var notes = new List<string>();
        var pngs = new List<string>();

        var progId = Type.GetTypeFromProgID("Visio.Application")
            ?? throw new InvalidOperationException(
                "Visio is not installed on this machine — the model was written, but nothing can draw it.");

        dynamic visio = Activator.CreateInstance(progId)!;
        try
        {
            visio.Visible = keepVisioOpen;
            visio.AlertResponse = 7;      // answer "No" to any prompt rather than blocking on it

            // AUTOMATION SWITCHES. Visio repaints, fires events and records an undo step for every
            // one of the ~9,000 shapes this draws, and none of that is wanted when nobody is
            // watching. Off, the render is a fraction of the time; the numbers are in the commit.
            visio.ScreenUpdating = 0;
            visio.EventsEnabled = 0;
            visio.DeferRecalc = 1;

            dynamic doc = visio.Documents.Add("");
            doc.UndoEnabled = false;

            notes.Add(PageApplication(doc, model));
            notes.Add(PageDrawingIntake(doc, model));
            notes.Add(MatrixDependencies(doc, model));
            notes.Add(MatrixFormats(doc, model));
            notes.Add(ListVerbs(doc, model));
            notes.Add(ListDuplication(doc, model));
            notes.Add(MasterMatrix(doc, model));
            notes.Add(ListScripts(doc, model));

            foreach (var g in model.Graphs)
            {
                bool recipe = g.Name == "Recipes";
                GraphPage(visio, doc, g, recipe ? 40 : 44, recipe ? 26 : 40, recipe);
                notes.Add($"graph: {g.Name} — {g.Nodes.Count} node(s), {g.Edges.Count} tie(s)");
            }

            // Recalc back ON before anything asks a page how big its contents are — that answer is
            // computed, and a deferred engine would size every page to stale geometry.
            visio.DeferRecalc = 0;
            foreach (dynamic p in doc.Pages) p.ResizeToFitContents();

            if (File.Exists(vsdxPath)) File.Delete(vsdxPath);
            doc.SaveAs(vsdxPath);

            foreach (dynamic p in doc.Pages)
            {
                string name = NotWord.Replace((string)p.Name, "-");
                string png = Path.Combine(outDir, $"KOR-Application-Map-{name}.png");
                if (File.Exists(png)) File.Delete(png);
                p.Export(png);
                pngs.Add(png);
            }

            doc.Close();
        }
        finally
        {
            if (!keepVisioOpen)
            {
                try { visio.Quit(); } catch (COMException) { }
            }
            Marshal.ReleaseComObject(visio);
        }

        return new RenderResult(vsdxPath, pngs, notes);
    }

    // ---- primitives --------------------------------------------------------------------------

    private static void SetPageSize(dynamic page, double w, double h, string name)
    {
        page.Name = name;
        page.PageSheet.CellsU["PageWidth"].FormulaU = $"{Inv(w)} in";
        page.PageSheet.CellsU["PageHeight"].FormulaU = $"{Inv(h)} in";
        page.PageSheet.CellsU["PageScale"].FormulaU = "1 in";
        page.PageSheet.CellsU["DrawingScale"].FormulaU = "1 in";
    }

    private static dynamic Box(dynamic page, double x, double y, double w, double h,
                               string text, string fill, double pt = 9, string? lineColor = null)
    {
        dynamic s = page.DrawRectangle(x, y, x + w, y + h);
        s.Text = text;
        s.CellsU["FillForegnd"].FormulaU = fill;
        s.CellsU["LineColor"].FormulaU = lineColor ?? Hairline;
        s.CellsU["LineWeight"].FormulaU = "0.5 pt";
        s.CellsU["Rounding"].FormulaU = "0.06 in";
        s.CellsU["Char.Size"].FormulaU = $"{Inv(pt)} pt";
        s.CellsU["Char.Color"].FormulaU = Ink;
        s.CellsU["Para.HorzAlign"].FormulaU = "1";
        s.CellsU["VerticalAlign"].FormulaU = "1";
        return s;
    }

    /// <summary>Wide by default: a label box narrower than its text wraps, and the subtitle came out
    /// on two lines overlapping the row of headers beneath it.</summary>
    private static dynamic Label(dynamic page, double x, double y, string text, double pt,
                                 string? color = null, double w = 30)
    {
        dynamic s = page.DrawRectangle(x, y, x + w, y + 0.34);
        s.Text = text;
        s.CellsU["LinePattern"].FormulaU = "0";
        s.CellsU["FillPattern"].FormulaU = "0";
        s.CellsU["Char.Size"].FormulaU = $"{Inv(pt)} pt";
        s.CellsU["Char.Color"].FormulaU = color ?? Ink;
        s.CellsU["Char.Style"].FormulaU = "1";
        s.CellsU["Para.HorzAlign"].FormulaU = "0";
        return s;
    }

    private static void Connect(dynamic visio, dynamic page, dynamic from, dynamic to,
                                string color, double weight = 0.5)
    {
        dynamic c = page.Drop(visio.ConnectorToolDataObject, 0, 0);
        c.CellsU["BeginX"].GlueTo(from.CellsU["PinX"]);
        c.CellsU["EndX"].GlueTo(to.CellsU["PinX"]);
        c.CellsU["LineColor"].FormulaU = color;
        c.CellsU["LineWeight"].FormulaU = $"{Inv(weight)} pt";
        c.CellsU["EndArrow"].FormulaU = "4";
        c.CellsU["EndArrowSize"].FormulaU = "1";
    }

    /// <summary>An unfilled, unbordered text cell — a row label, a column heading. `angle` rotates it
    /// so a long project name does not need a two-inch column.</summary>
    private static void TextCell(dynamic page, double x1, double y1, double x2, double y2,
                                 string text, double pt, string align, double? angle = null)
    {
        dynamic s = page.DrawRectangle(x1, y1, x2, y2);
        s.Text = text;
        s.CellsU["LinePattern"].FormulaU = "0";
        s.CellsU["FillPattern"].FormulaU = "0";
        s.CellsU["Char.Size"].FormulaU = $"{Inv(pt)} pt";
        s.CellsU["Char.Color"].FormulaU = Ink;
        s.CellsU["Para.HorzAlign"].FormulaU = align;
        if (align != "0") s.CellsU["VerticalAlign"].FormulaU = "1";
        if (angle.HasValue) s.CellsU["Angle"].FormulaU = $"{Inv(angle.Value)} deg";
    }

    // ---- page 1: the whole application -------------------------------------------------------

    private static string PageApplication(dynamic doc, ArchModel model)
    {
        dynamic page = doc.Pages[1];
        SetPageSize(page, 46, 32, "Application");

        var shapes = new Dictionary<string, dynamic>(StringComparer.Ordinal);
        const double boxW = 3.9, boxH = 1.0, gapX = 0.32, gapY = 0.30, left = 0.9;
        double y = 30.4;

        Label(page, left, y + 0.7, "KOR Operations — the whole application", 20, Accent);
        Label(page, left, y + 0.25,
            "generated from source by Kor.Operations.Architecture — do not edit this file, edit the renderer   ·   " +
            $"{model.Projects.Count} projects · {N0(model.Stats.Lines)} lines · {model.Types.Count} types",
            10, Hairline);

        foreach (string cluster in ClusterOrder)
        {
            var inCluster = model.Projects.Where(p => p.Cluster == cluster)
                .OrderByDescending(p => p.Lines).ToList();
            if (inCluster.Count == 0) continue;

            y -= 0.62;
            Label(page, left, y,
                $"{cluster}  —  {inCluster.Count} project(s), {N0(inCluster.Sum(p => p.Lines))} lines", 12);
            y -= boxH + 0.12;

            double x = left;
            foreach (var p in inCluster)
            {
                if (x + boxW > 45.2) { x = left; y -= boxH + gapY; }
                shapes[p.Name] = Box(page, x, y, boxW, boxH,
                    $"{p.Name}\n{N0(p.Lines)} lines · {p.Files} files", ClusterFill[cluster], 9);
                x += boxW + gapX;
            }
            y -= 0.55;
        }

        // The one-off tools are ONE box, not thirty-four. They are real, they are not architecture,
        // and drawing each of them buries the seven things on this page that matter.
        var tools = model.Projects.Where(p => p.Cluster == "one-off tools").ToList();
        if (tools.Count > 0)
        {
            y -= 0.5;
            Box(page, left, y, boxW * 2 + gapX, boxH,
                $"tools/  —  {tools.Count} one-off tools\n{N0(tools.Sum(p => p.Lines))} lines · see the CLI verbs page",
                ClusterFill["one-off tools"], 10);
            y -= 0.55;
        }

        y -= 0.45;
        Label(page, left, y, "outside this repository", 12);
        y -= boxH + 0.12;
        double ex = left;
        foreach (var e in model.Externals)
        {
            if (ex + 3.0 > 45.2) { ex = left; y -= 0.8 + gapY; }
            shapes["ext:" + e.Name] = Box(page, ex, y, 3.0, 0.8,
                $"{e.Name}\n{e.Kind} · {e.Evidence.Count} file(s)", "RGB(255,249,230)", 9, "RGB(190,150,60)");
            ex += 3.0 + gapX;
        }

        int edges = 0;
        dynamic visio = doc.Application;
        foreach (var p in model.Projects)
        {
            if (!shapes.TryGetValue(p.Name, out var from)) continue;
            foreach (string r in p.ProjectRefs)
            {
                if (!shapes.TryGetValue(r, out var to)) continue;
                Connect(visio, page, from, to, "RGB(175,185,195)", 0.4);
                edges++;
            }
        }
        return $"page 1: {shapes.Count} shape(s), {edges} reference edge(s)";
    }

    // ---- page 2: the convergence -------------------------------------------------------------

    private static string PageDrawingIntake(dynamic doc, ArchModel model)
    {
        dynamic page = doc.Pages.Add();
        SetPageSize(page, 44, 30, "Drawing intake");
        dynamic visio = doc.Application;

        var spine = model.Types
            .Where(t => t.Namespace.Contains("EngineeringTools", StringComparison.Ordinal))
            .Where(t => t.Role is "read" or "compose" or "classify" or "write")
            .ToList();

        Label(page, 0.9, 28.9, "Drawing intake — where the tools converge", 20, Accent);
        Label(page, 0.9, 28.45,
            "a drawing arrives in one of five formats, is READ into one geometry model, and is WRITTEN out to another — " +
            $"{spine.Count} types", 10, Hairline);

        double[] colX = { 0.9, 9.6, 19.4, 29.6, 38.4 };
        double[] colW = { 7.6, 8.6, 9.0, 7.6, 4.6 };
        string[] colHead = { "arrives as", "read by", "held as", "written by", "ships as" };
        for (int i = 0; i < colHead.Length; i++) Label(page, colX[i], 27.6, colHead[i], 13);

        Dictionary<string, dynamic> Stack(int col, IEnumerable<string> labels, string fill, double pt = 9)
        {
            var outp = new Dictionary<string, dynamic>(StringComparer.Ordinal);
            double yy = 27.0;
            const double h = 0.62;
            foreach (string l in labels)
            {
                outp[l] = Box(page, colX[col], yy - h, colW[col], h, l, fill, pt);
                yy -= h + 0.16;
            }
            return outp;
        }

        var spineIds = spine.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var readExt = model.Formats
            .Where(f => spineIds.TryGetValue(f.Type, out var t) && t.Role == "read")
            .Select(f => f.Ext).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var writeExt = model.Formats
            .Where(f => spineIds.TryGetValue(f.Type, out var t) && t.Role == "write")
            .Select(f => f.Ext).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var inBoxes = Stack(0, readExt, "RGB(255,249,230)", 11);
        var readers = Stack(1, Names(spine, r => r == "read"), ClusterFill["drawing intake"]);
        var middle = Stack(2, Names(spine, r => r is "compose" or "classify"), "RGB(226,240,226)");
        var writers = Stack(3, Names(spine, r => r == "write"), "RGB(252,236,219)");
        var outBoxes = Stack(4, writeExt, "RGB(255,249,230)", 11);

        int spineEdges = 0;
        foreach (var f in model.Formats)
        {
            if (!spineIds.TryGetValue(f.Type, out var t)) continue;
            if (t.Role == "read" && inBoxes.TryGetValue(f.Ext, out var ib) && readers.TryGetValue(t.Name, out var rb))
            { Connect(visio, page, ib, rb, "RGB(190,160,90)", 0.4); spineEdges++; }
            if (t.Role == "write" && outBoxes.TryGetValue(f.Ext, out var ob) && writers.TryGetValue(t.Name, out var wb))
            { Connect(visio, page, wb, ob, "RGB(190,160,90)", 0.4); spineEdges++; }
        }

        // EVERY DIRECT MENTION BETWEEN TWO SPINE TYPES, whatever their roles. Drawing only the role
        // pairs that matched a pipeline in my head got 8 arrows out of 34 real ones — and threw away
        // all ten READER→READER edges, which are the single most valuable thing on this page.
        var boxOf = new Dictionary<string, (dynamic Box, int Col)>(StringComparer.Ordinal);
        foreach (var kv in readers) boxOf[kv.Key] = (kv.Value, 1);
        foreach (var kv in middle) boxOf[kv.Key] = (kv.Value, 2);
        foreach (var kv in writers) boxOf[kv.Key] = (kv.Value, 3);

        var nameOf = spine.ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);
        int sameColumn = 0;
        foreach (var e in model.Mentions)
        {
            if (!nameOf.TryGetValue(e.From, out string? a) || !nameOf.TryGetValue(e.To, out string? b)) continue;
            if (!boxOf.TryGetValue(a, out var pa) || !boxOf.TryGetValue(b, out var pb)) continue;

            // A within-column edge is a type leaning on another that does the same KIND of job —
            // exactly what a convergence review is looking for — so it gets its own colour and weight.
            if (pa.Col == pb.Col) { Connect(visio, page, pa.Box, pb.Box, "RGB(200,80,40)", 1.0); sameColumn++; }
            else Connect(visio, page, pa.Box, pb.Box, "RGB(120,150,180)", 0.5);
            spineEdges++;
        }

        return $"page 2: {spine.Count} type(s), {spineEdges} edge(s) ({sameColumn} within a column — the convergence signal)";

        static List<string> Names(List<ArchType> spine, Func<string, bool> role)
            => spine.Where(t => role(t.Role)).Select(t => t.Name)
                    .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    // ---- matrices ----------------------------------------------------------------------------

    /// <summary>A SPARSE matrix: only cells that carry a value are drawn. A full 63×63 grid is four
    /// thousand COM round trips to say "mostly nothing". Row bands give the eye the grid instead.</summary>
    private static void MatrixPage(dynamic doc, string name, string title, string subtitle,
                                   IReadOnlyList<string> rows, IReadOnlyList<string> cols,
                                   Dictionary<string, string> cells, string fill)
    {
        dynamic page = doc.Pages.Add();
        const double rowW = 4.6, colW = 1.30, cellH = 0.42, headH = 3.4;
        double w = rowW + cols.Count * colW + 1.6;
        double h = headH + rows.Count * cellH + 1.6;
        SetPageSize(page, Math.Max(w, 12), Math.Max(h, 10), name);

        double top = h - 1.1;
        Label(page, 0.7, top, title, 18, Accent);
        Label(page, 0.7, top - 0.45, subtitle, 10, Hairline);

        double gridTop = top - 0.95;
        double x0 = 0.7 + rowW;

        for (int c = 0; c < cols.Count; c++)
            TextCell(page, x0 + c * colW, gridTop - 2.0, x0 + c * colW + 2.0, gridTop - 2.0 + 0.28,
                     Short(cols[c]), 8, "0", 60);

        double gridBottom = gridTop - 2.15;
        for (int r = 0; r < rows.Count; r++)
        {
            double y = gridBottom - (r + 1) * cellH;

            if (r % 2 == 0)
            {
                dynamic band = page.DrawRectangle(0.7, y, x0 + cols.Count * colW, y + cellH);
                band.CellsU["FillForegnd"].FormulaU = "RGB(246,248,250)";
                band.CellsU["LinePattern"].FormulaU = "0";
                band.SendToBack();
            }

            TextCell(page, 0.7, y, x0 - 0.1, y + cellH, Short(rows[r]), 8.5, "2");

            for (int c = 0; c < cols.Count; c++)
            {
                if (!cells.TryGetValue(rows[r] + "||" + cols[c], out string? v)) continue;
                dynamic cell = page.DrawRectangle(x0 + c * colW + 0.06, y + 0.03,
                                                  x0 + c * colW + colW - 0.06, y + cellH - 0.03);
                cell.Text = v;
                cell.CellsU["FillForegnd"].FormulaU = fill;
                cell.CellsU["LineColor"].FormulaU = Hairline;
                cell.CellsU["LineWeight"].FormulaU = "0.25 pt";
                cell.CellsU["Char.Size"].FormulaU = "8 pt";
                cell.CellsU["Char.Color"].FormulaU = Ink;
                cell.CellsU["Para.HorzAlign"].FormulaU = "1";
                cell.CellsU["VerticalAlign"].FormulaU = "1";
            }
        }
    }

    private static string MatrixDependencies(dynamic doc, ArchModel model)
    {
        var real = model.Projects.Where(p => p.Cluster != "one-off tools")
            .OrderBy(p => p.Cluster, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.Name).ToList();
        var set = real.ToHashSet(StringComparer.Ordinal);

        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in model.Projects.Where(p => set.Contains(p.Name)))
            foreach (string r in p.ProjectRefs.Where(set.Contains))
                cells[p.Name + "||" + r] = "•";

        MatrixPage(doc, "Matrix - dependencies", "Which project depends on which",
            $"read a ROW: this project references these. {cells.Count} reference(s) across {real.Count} projects, " +
            $"{model.Cycles.Count} cycle(s). The one-off tools are left out.",
            real, real, cells, "RGB(210,228,244)");
        return $"matrix: dependencies — {cells.Count} cell(s)";
    }

    /// <summary>THE EFFICIENCY VIEW. One format handled in four projects is four answers to one
    /// question.</summary>
    private static string MatrixFormats(dynamic doc, ArchModel model)
    {
        var cells = new Dictionary<string, int>(StringComparer.Ordinal);
        var cols = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in model.Formats)
        {
            string proj = f.Type.Split(':')[0];
            cols.Add(proj);
            string k = f.Ext + "||" + proj;
            cells[k] = cells.TryGetValue(k, out int n) ? n + 1 : 1;
        }
        var rows = model.Formats.Select(f => f.Ext).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        MatrixPage(doc, "Matrix - formats", "Which project handles which file format",
            "the number is HOW MANY TYPES in that project touch that format. A format with several " +
            $"columns is the same question answered in several places — {model.Formats.Count} format edges.",
            rows, cols.ToList(),
            cells.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal),
            "RGB(252,232,206)");
        return $"matrix: formats — {cells.Count} cell(s)";
    }

    // ---- list pages --------------------------------------------------------------------------

    private static void ListPage(dynamic doc, string name, string title, string subtitle,
                                 IReadOnlyList<string> lines, string fill, int perColumn = 40)
    {
        dynamic page = doc.Pages.Add();
        const double colW = 9.4, rowH = 0.34;
        int colCount = Math.Max(1, (int)Math.Ceiling(lines.Count / (double)perColumn));
        double h = perColumn * rowH + 2.6;
        SetPageSize(page, Math.Max(colCount * colW + 1.6, 12), h, name);

        Label(page, 0.7, h - 1.1, title, 18, Accent);
        Label(page, 0.7, h - 1.55, subtitle, 10, Hairline);

        for (int i = 0; i < lines.Count; i++)
        {
            int col = i / perColumn, row = i % perColumn;
            Box(page, 0.7 + col * colW, (h - 2.2) - (row + 1) * rowH, colW - 0.3, rowH - 0.05,
                lines[i], fill, 8.5);
        }
    }

    private static string ListVerbs(dynamic doc, ArchModel model)
    {
        var lines = model.Verbs.Select(v => $"{v.Verb}   ·   {Short(v.Project)}").ToList();
        ListPage(doc, "CLI verbs", "Every command-line verb",
            $"{model.Verbs.Count} verb(s), read off args[0].Equals(\"…\") rather than grepped for",
            lines, "RGB(226,240,226)", 24);
        return $"list: {model.Verbs.Count} CLI verb(s)";
    }

    /// <summary>Names every console tool is entitled to are excluded: seventeen `Program`s is not a
    /// finding.</summary>
    private static readonly HashSet<string> Boilerplate = new(StringComparer.Ordinal)
        { "Program", "ToolOptions", "ImportOptions", "ImportStats", "ImportConfig", "Options", "Result" };

    private static string ListDuplication(dynamic doc, ArchModel model)
    {
        var dupes = model.Duplicates.Where(d => !Boilerplate.Contains(d.Name)).ToList();
        var lines = dupes.Select(d =>
            $"{d.Similarity * 100,3:0}%  {d.Lines,4} lines  {d.Name}  —  {d.Projects.Count}x:  " +
            string.Join(", ", d.Projects.Select(Short))).ToList();

        int near = dupes.Count(d => d.Similarity >= 0.90);
        ListPage(doc, "Duplication", "One name, more than one project",
            $"{dupes.Count} name(s) after excluding per-tool boilerplate ({model.Duplicates.Count} before). " +
            $"The percentage is how alike the DECLARATIONS actually are — {near} are 90%+ identical, " +
            $"{dupes.Count(d => d.Similarity < 0.55)} share nothing but the name. " +
            $"{dupes.Where(d => d.Similarity >= 0.90).Sum(d => d.Lines)} duplicated lines in the 90%+ group.",
            lines, "RGB(250,224,216)", 30);
        return $"list: {dupes.Count} duplicated name(s), {near} of them 90%+ identical";
    }

    /// <summary>A map built from .csproj and .cs alone claims this system is one language. It is not:
    /// PowerShell deploys it, Python checks its shipped PDFs, SQL migrates its database.</summary>
    private static string ListScripts(dynamic doc, ArchModel model)
    {
        var scripts = model.Scripts.Where(s => s.Kind != "SQL migration").ToList();
        var orphans = scripts.Where(s => s.ReferencedBy == 0).OrderByDescending(s => s.Lines).ToList();
        int migrations = model.Scripts.Count - scripts.Count;

        var lines = orphans.Select(s => $"{s.Lines,5} lines  {s.Kind,-12} {s.Path}").ToList();
        ListPage(doc, "Nooks and crannies", "Scripts nothing references",
            $"{scripts.Count} script(s) live outside every project — " +
            $"{scripts.Count(s => s.Kind == "PowerShell")} PowerShell, {scripts.Count(s => s.Kind == "Python")} Python, " +
            $"{scripts.Count(s => s.Kind == "SQL")} SQL. These {orphans.Count} are named by NO other file in the " +
            "repository: dead, or run by a person from memory. The " + migrations +
            " numbered SQL migrations are excluded — a runner applies those in order and nothing names them.",
            lines, "RGB(238,232,244)", 46);
        return $"list: {orphans.Count} unreferenced script(s) of {scripts.Count} (+ {migrations} migrations)";
    }

    // ---- the master sheet --------------------------------------------------------------------

    private sealed record Block(string Title, IReadOnlyList<string> Cols,
                                Dictionary<string, int> Cells, string Fill, double ColW, bool Shorten);

    /// <summary>Every project is a row and four blocks share that row axis. Read ACROSS for one
    /// project's whole profile, DOWN for everyone who touches one thing. Sharing the row axis is the
    /// point: two rows with the same pattern are two projects doing the same job.</summary>
    private static string MasterMatrix(dynamic doc, ArchModel model)
    {
        var rows = model.Projects
            .OrderBy(p => p.Cluster, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
        var names = rows.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var dep = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in model.Projects)
            foreach (string r in p.ProjectRefs.Where(names.Contains))
                dep[p.Name + "||" + r] = 1;

        var fmt = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in model.Formats)
        {
            string k = f.Type.Split(':')[0] + "||" + f.Ext;
            fmt[k] = fmt.TryGetValue(k, out int n) ? n + 1 : 1;
        }

        var byLongestDir = model.Projects.OrderByDescending(p => p.Dir.Length).ToList();
        var ext = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in model.Externals)
            foreach (string ev in e.Evidence)
            {
                var owner = byLongestDir.FirstOrDefault(p =>
                    ev.StartsWith(p.Dir + "/", StringComparison.OrdinalIgnoreCase));
                if (owner is null) continue;
                string k = owner.Name + "||" + e.Name;
                ext[k] = ext.TryGetValue(k, out int n) ? n + 1 : 1;
            }

        var role = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in model.Types)
        {
            string k = t.Project + "||" + t.Role;
            role[k] = role.TryGetValue(k, out int n) ? n + 1 : 1;
        }

        var blocks = new List<Block>
        {
            new("depends on", rows.Select(p => p.Name).ToList(), dep, "RGB(210,228,244)", 0.44, true),
            new("file formats",
                model.Formats.Select(f => f.Ext).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                fmt, "RGB(252,232,206)", 0.56, false),
            new("outside the repo", model.Externals.Select(e => e.Name).ToList(), ext, "RGB(255,240,200)", 0.62, false),
            new("types by role",
                new[] { "read", "compose", "classify", "write", "service", "ui", "config", "model", "test" },
                role, "RGB(224,240,224)", 0.62, false),
        };

        const double rowH = 0.34, labelW = 4.4, blockGap = 0.85;
        double gridW = labelW;
        foreach (var b in blocks) gridW += b.Cols.Count * b.ColW + blockGap;
        double sheetH = rows.Count * rowH + 5.2;
        double sheetW = gridW + 1.4;

        // MASTER is a reserved page name in Visio.
        dynamic page = doc.Pages.Add();
        SetPageSize(page, sheetW, sheetH, "Master matrix");

        Label(page, 0.7, sheetH - 1.0, "KOR Operations — master matrix", 22, Accent);
        Label(page, 0.7, sheetH - 1.5,
            "every project in the repository is a row. read ACROSS for one project's whole profile, DOWN for everyone who touches one thing.   ·   " +
            $"{model.Projects.Count} projects · {N0(model.Stats.Lines)} lines · {model.Types.Count} types · " +
            $"{model.Verbs.Count} CLI verbs · {model.Cycles.Count} dependency cycles", 10, Hairline);

        double gridTop = sheetH - 4.0;
        int filled = 0;

        double bx = 0.7 + labelW;
        foreach (var b in blocks)
        {
            Label(page, bx, gridTop + 1.55, b.Title, 12, Accent);
            for (int c = 0; c < b.Cols.Count; c++)
                TextCell(page, bx + c * b.ColW, gridTop, bx + c * b.ColW + 1.5, gridTop + 0.26,
                         b.Shorten ? Short(b.Cols[c]) : b.Cols[c], 7.5, "0", 60);
            bx += b.Cols.Count * b.ColW + blockGap;
        }

        for (int r = 0; r < rows.Count; r++)
        {
            var proj = rows[r];
            double y = gridTop - (r + 1) * rowH;

            // The row band is the project's CLUSTER colour, so the sheet groups itself.
            dynamic band = page.DrawRectangle(0.7, y, gridW + 0.7 - blockGap, y + rowH);
            band.CellsU["FillForegnd"].FormulaU = ClusterFill.TryGetValue(proj.Cluster, out string? cf) ? cf : "RGB(240,240,240)";
            band.CellsU["FillPattern"].FormulaU = r % 2 == 0 ? "1" : "0";
            band.CellsU["LinePattern"].FormulaU = "0";
            band.SendToBack();

            TextCell(page, 0.7, y, 0.7 + labelW - 0.12, y + rowH,
                     $"{Short(proj.Name)}   ·   {N0(proj.Lines)}", 8, "2");

            bx = 0.7 + labelW;
            foreach (var b in blocks)
            {
                for (int c = 0; c < b.Cols.Count; c++)
                {
                    if (!b.Cells.TryGetValue(proj.Name + "||" + b.Cols[c], out int v)) continue;
                    dynamic cell = page.DrawRectangle(bx + c * b.ColW + 0.04, y + 0.035,
                                                      bx + c * b.ColW + b.ColW - 0.04, y + rowH - 0.035);
                    cell.Text = b.Title == "depends on" ? "•" : v.ToString(CultureInfo.InvariantCulture);
                    cell.CellsU["FillForegnd"].FormulaU = b.Fill;
                    cell.CellsU["LineColor"].FormulaU = Hairline;
                    cell.CellsU["LineWeight"].FormulaU = "0.25 pt";
                    cell.CellsU["Char.Size"].FormulaU = "7.5 pt";
                    cell.CellsU["Char.Color"].FormulaU = Ink;
                    cell.CellsU["Para.HorzAlign"].FormulaU = "1";
                    cell.CellsU["VerticalAlign"].FormulaU = "1";
                    filled++;
                }
                bx += b.Cols.Count * b.ColW + blockGap;
            }
        }

        return $"MASTER: {rows.Count} rows x {blocks.Sum(b => b.Cols.Count)} columns, {filled} filled cell(s), " +
               $"{sheetW:N0} x {sheetH:N0} in";
    }

    // ---- graph pages -------------------------------------------------------------------------

    /// <summary>Nodes where the layout put them, ties drawn STRAIGHT. A routed connector is right when
    /// a diagram is boxes in rows; on a force-directed graph it fights the layout, adds elbows the
    /// layout did not ask for, and costs a COM round trip each.</summary>
    private static void GraphPage(dynamic visio, dynamic doc, ArchGraph graph, double w, double h, bool recipe)
    {
        dynamic page = doc.Pages.Add();
        SetPageSize(page, w, h, graph.Name);

        Label(page, 0.8, h - 1.1, graph.Title, 22, Accent);
        Label(page, 0.8, h - 1.6, graph.Subtitle, 10.5, Hairline);

        const double margin = 1.6;
        double plotW = w - 2 * margin, plotH = h - margin - 2.6;
        var at = graph.Nodes.ToDictionary(
            n => n.Id,
            n => (X: margin + n.X * plotW, Y: margin + n.Y * plotH),
            StringComparer.Ordinal);

        // EDGES FIRST, so nodes sit on top of them rather than under.
        foreach (var e in graph.Edges)
        {
            if (!at.TryGetValue(e.From, out var a) || !at.TryGetValue(e.To, out var b)) continue;
            dynamic line = page.DrawLine(a.X, a.Y, b.X, b.Y);
            string kind = e.Kind.Split(':')[0];
            switch (kind)
            {
                case "duplicates":
                    int n = int.TryParse(e.Kind.Split(':').ElementAtOrDefault(1), out int c) ? c : 1;
                    line.CellsU["LineColor"].FormulaU = "RGB(205,60,45)";
                    line.CellsU["LineWeight"].FormulaU = $"{Inv(Math.Min(4.0, 0.8 * n))} pt";
                    line.CellsU["LinePattern"].FormulaU = "1";
                    break;
                case "talks to":
                    line.CellsU["LineColor"].FormulaU = "RGB(215,175,60)";
                    line.CellsU["LineWeight"].FormulaU = "0.5 pt";
                    break;
                case "same rank":
                    line.CellsU["LineColor"].FormulaU = "RGB(205,60,45)";
                    line.CellsU["LineWeight"].FormulaU = "1.0 pt";
                    line.CellsU["EndArrow"].FormulaU = "4";
                    break;
                default:
                    line.CellsU["LineColor"].FormulaU = "RGB(120,132,145)";
                    line.CellsU["LineWeight"].FormulaU = "0.6 pt";
                    if (recipe) line.CellsU["EndArrow"].FormulaU = "4";
                    break;
            }
        }

        foreach (var node in graph.Nodes)
        {
            var c = at[node.Id];
            string fill = GraphFill.TryGetValue(node.Group, out string? f) ? f : "RGB(200,205,210)";
            dynamic s;

            if (recipe)
            {
                // An ARTEFACT is a thing you can hold, so it is a rectangle. An OPERATION is
                // something that happens to it, so it is a diamond. That is the whole legend.
                const double hw = 1.55, hh = 0.30;
                if (node.Group == "artefact")
                {
                    s = page.DrawRectangle(c.X - hw, c.Y - hh, c.X + hw, c.Y + hh);
                    s.CellsU["Rounding"].FormulaU = "0.05 in";
                }
                else
                {
                    // Visio wants a SAFEARRAY of doubles here — a typed double[], not object[].
                    double[] pts =
                    {
                        c.X - hw, c.Y, c.X, c.Y + hh,
                        c.X + hw, c.Y, c.X, c.Y - hh,
                        c.X - hw, c.Y,
                    };
                    s = page.DrawPolyline(pts, 0);
                }
                s.Text = node.Label;
                s.CellsU["Char.Size"].FormulaU = "8 pt";
            }
            else
            {
                // Area in proportion to size, so a 92,000-line project reads as bigger without being
                // ninety-two times wider than a 1,000-line one.
                double r = 0.16 + 1.05 * node.Weight;
                s = page.DrawOval(c.X - r, c.Y - r, c.X + r, c.Y + r);
                s.Text = node.Label;
                s.CellsU["Char.Size"].FormulaU = $"{Inv(Math.Max(6.5, Math.Min(11, 5 + r * 5)))} pt";
            }

            s.CellsU["FillForegnd"].FormulaU = fill;
            s.CellsU["LineColor"].FormulaU = "RGB(70,80,92)";
            s.CellsU["LineWeight"].FormulaU = "0.5 pt";
            s.CellsU["Char.Color"].FormulaU = Ink;
            s.CellsU["Para.HorzAlign"].FormulaU = "1";
            s.CellsU["VerticalAlign"].FormulaU = "1";
        }
    }
}
