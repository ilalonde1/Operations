// THE MAP IS DERIVED FROM THE CODE, NEVER DRAWN.
//
// A hand-drawn architecture diagram is wrong the week after it is drawn. In the single session that
// prompted this one, ~460 lines moved from the App into VectorPageReader, PdfGeometryParser became a
// projection over it, SheetScaleReader grew a method, DxfPositionedTag grew a field and dxf-render
// grew a flag. Five structural facts, one day. A picture drawn that morning would have been wrong
// three times by the evening, and a confidently wrong map is worse than none — it is the input that
// produced four days of single-symptom patching on the storey question.
//
// So: this reads the repository and emits a MODEL. The model is committed as text, so `git diff`
// shows the architecture moving. A test compares what this extracts against what is committed and
// fails, naming what moved. The Visio file is RENDERED from the model and is an output — editing it
// is pointless, the next run overwrites it.
//
// SYNTAX TREES, NOT A COMPILATION. No MSBuildWorkspace and no Build.Locator, so this runs whether or
// not the solution currently compiles, needs no restore, and takes seconds over 291k lines. The cost
// is honest and stated in the model itself: a mention edge is "this file names that type", which
// cannot resolve an overload or an extension-method target. For a map, naming is what we want.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kor.Operations.Architecture;

public static class Program
{
    public static int Main(string[] rawArgs)
    {
        string root = ArgValue(rawArgs, "--root") ?? Directory.GetCurrentDirectory();
        string outPath = ArgValue(rawArgs, "--out")
                         ?? Path.Combine(root, "docs", "architecture", "architecture.json");

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"root not found: {root}");
            return 2;
        }

        var model = Extractor.Extract(root);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        string json = JsonSerializer.Serialize(model, JsonOptions);
        // Normalise the line ending so the committed file is stable whichever machine wrote it.
        File.WriteAllText(outPath, json.ReplaceLineEndings("\n") + "\n", new UTF8Encoding(false));

        Console.WriteLine($"{model.Projects.Count} project(s), {model.Types.Count} type(s), " +
                          $"{model.Mentions.Count} mention edge(s), {model.Formats.Count} format edge(s), " +
                          $"{model.Externals.Count} external(s), {model.Verbs.Count} CLI verb(s)");
        Console.WriteLine($"  {model.Stats.Files:N0} files, {model.Stats.Lines:N0} lines, " +
                          $"{model.Stats.AmbiguousTypeNames} ambiguous type name(s) not linked");
        Console.WriteLine($"  {model.Scripts.Count} script(s) outside any project, " +
                          $"{model.Scripts.Count(s => s.ReferencedBy == 0)} referenced by nothing");
        Console.WriteLine($"wrote {outPath}");

        if (Flag(rawArgs, "--model-only")) return 0;

        // ---- DRAW IT ------------------------------------------------------------------------
        string outDir = Path.GetDirectoryName(outPath)!;
        Console.WriteLine();
        Console.WriteLine("rendering…");

        RenderResult render;
        try
        {
            render = VisioRenderer.Render(model, outDir, keepVisioOpen: Flag(rawArgs, "--keep-open"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // The model is the durable artefact and it is already written. Failing to draw is worth
            // a non-zero exit, but not worth throwing away the extraction that succeeded.
            Console.Error.WriteLine($"could not render: {ex.Message}");
            return 3;
        }

        foreach (string note in render.Notes) Console.WriteLine("  " + note);

        // ---- ONE COMMAND MEASURES EVERY DELIVERABLE -------------------------------------------
        Console.WriteLine();
        Console.WriteLine("wrote:");
        var produced = new List<string> { outPath, render.VsdxPath };
        produced.AddRange(render.PngPaths);

        var bad = new List<string>();
        foreach (string f in produced)
        {
            if (!File.Exists(f)) { bad.Add($"missing: {Path.GetFileName(f)}"); Console.WriteLine($"  {Path.GetFileName(f),-52} MISSING"); continue; }
            long size = new FileInfo(f).Length;
            Console.WriteLine($"  {Path.GetFileName(f),-52} {size,9:N0} bytes");
            if (size < 4096) bad.Add($"suspiciously small: {Path.GetFileName(f)}");
        }

        if (!Flag(rawArgs, "--verify")) return 0;

        if (render.PngPaths.Count < 2) bad.Add($"only {render.PngPaths.Count} page(s) exported; expected at least 2");
        if (bad.Count > 0)
        {
            foreach (string b in bad) Console.Error.WriteLine("  FAIL " + b);
            return 1;
        }
        Console.WriteLine($"verify: {render.PngPaths.Count} page(s) and both outputs present");
        return 0;
    }

    private static bool Flag(string[] args, string name)
        => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string? ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}

// ---------------------------------------------------------------------------------------------
// The model. Everything is sorted before it is written, and NOTHING carries a timestamp: a
// generation time in here would make every diff fire and the freshness test meaningless.
// ---------------------------------------------------------------------------------------------

public sealed record ArchModel(
    int Schema,
    IReadOnlyList<ArchProject> Projects,
    IReadOnlyList<ArchType> Types,
    IReadOnlyList<ArchEdge> Mentions,
    IReadOnlyList<ArchFormat> Formats,
    IReadOnlyList<ArchExternal> Externals,
    IReadOnlyList<ArchVerb> Verbs,
    IReadOnlyList<ArchDuplicate> Duplicates,
    IReadOnlyList<ArchOrphan> Orphans,
    IReadOnlyList<ArchCycle> Cycles,
    IReadOnlyList<ArchGraph> Graphs,
    IReadOnlyList<ArchScript> Scripts,
    ArchStats Stats);

public sealed record ArchProject(
    string Name,
    string Dir,
    string Cluster,
    string Frameworks,
    IReadOnlyList<string> ProjectRefs,
    IReadOnlyList<string> Packages,
    int Files,
    int Lines);

public sealed record ArchType(
    string Id,
    string Name,
    string Kind,
    string Namespace,
    string Project,
    string File,
    string Role);

public sealed record ArchEdge(string From, string To);

/// <summary><paramref name="Source"/> is how the edge was established — "name", "using" or
/// "literal" — so every arrow on the diagram can be traced back to why it is there.</summary>
public sealed record ArchFormat(string Type, string Ext, string Direction, string Source);

public sealed record ArchExternal(string Name, string Kind, IReadOnlyList<string> Evidence);

public sealed record ArchVerb(string Verb, string Project);

public sealed record ArchStats(int Files, int Lines, int AmbiguousTypeNames);

/// <summary>One type NAME declared in more than one project.
///
/// A shared name is where to LOOK, not a finding — two unrelated `Contact` records prove nothing. So
/// <paramref name="Similarity"/> is the real measure: the declarations are pulled out of their files
/// and compared as text, and this is the closest pair, 0 to 1. On this repo 20 of 38 come out above
/// 0.9 and 10 share nothing but the name, which is the difference between a list and a finding.
///
/// <paramref name="Lines"/> is the larger declaration's length, so the cost is visible: a 93-line
/// type at 0.85 is worth more attention than a 1-line record at 0.97.</summary>
public sealed record ArchDuplicate(
    string Name,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Files,
    double Similarity,
    int Lines);

/// <summary>A type no other type names. A CANDIDATE, not a verdict — a type reached only through
/// XAML, DI, reflection or a generic parameter is invisible to a syntax-level read, and this counts
/// it as unreferenced. Useful as a list to walk, never as an instruction to delete.</summary>
public sealed record ArchOrphan(string Id, string Name, string Kind, string Project, string File);

/// <summary>Projects that reference each other round a loop. There should be none.</summary>
public sealed record ArchCycle(IReadOnlyList<string> Projects);

/// <summary>A node-link view: everything and what it is connected to, laid out so that things which
/// pull on each other end up near each other. Positions are computed HERE and not in the renderer,
/// because Visio has no force-directed layout and a graph drawn in rows is not a graph.</summary>
public sealed record ArchGraph(
    string Name,
    string Title,
    string Subtitle,
    IReadOnlyList<ArchNode> Nodes,
    IReadOnlyList<ArchGraphEdge> Edges);

public sealed record ArchNode(
    string Id, string Label, string Detail, string Group, double Weight, double X, double Y);

public sealed record ArchGraphEdge(string From, string To, string Kind);

// ---------------------------------------------------------------------------------------------

public static class Extractor
{
    public static ArchModel Extract(string root)
    {
        var projects = ReadProjects(root);
        var byDir = projects
            .OrderByDescending(p => p.Dir.Length)   // longest first: a file belongs to its nearest project
            .ToList();

        var typeParts = new Dictionary<string, TypeParts>(StringComparer.Ordinal);
        var mentionsRaw = new List<(string From, string ToName)>();
        var formatsRaw = new List<(string TypeId, string Ext, string Source)>();
        var verbs = new List<ArchVerb>();
        var externalHits = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        // Kept in memory only, never serialised: this is 292k lines of source and the point of it is
        // the single similarity number it produces, not the text.
        var declarations = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var fileCounts = new Dictionary<string, (int Files, int Lines)>(StringComparer.OrdinalIgnoreCase);
        int totalFiles = 0, totalLines = 0;
        var compiledBy = CompileOwners(root, projects, byDir);

        foreach (string file in EnumerateSources(root))
        {
            string rel = Rel(root, file);
            var owners = compiledBy.TryGetValue(rel, out var ownerSet) && ownerSet.Count > 0
                ? ownerSet.ToList()
                : new List<string> { "(loose)" };

            string text;
            try { text = TextFiles.ReadAllText(file); }
            catch (IOException) { continue; }
            catch (DecoderFallbackException) { continue; }

            int lines = CountLines(text);
            totalFiles++;
            totalLines += lines;

            foreach (var (name, evidence) in ExternalSystems.Detect(text, rel))
            {
                if (!externalHits.TryGetValue(name, out var set))
                    externalHits[name] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(evidence);
            }

            var tree = CSharpSyntaxTree.ParseText(text, path: file);
            var rootNode = tree.GetRoot();

            var usings = rootNode.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.Name?.ToString() ?? "")
                .Where(u => u.Length > 0)
                .ToList();

            foreach (var verb in CliVerbs(rootNode))
                foreach (string projectName in owners)
                    verbs.Add(new ArchVerb(verb, projectName));

            foreach (var decl in rootNode.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                string name = decl.Identifier.ValueText;
                string ns = NamespaceOf(decl);
                string kind = decl switch
                {
                    ClassDeclarationSyntax cls when cls.Modifiers.Any(SyntaxKind.StaticKeyword) => "static class",
                    ClassDeclarationSyntax => "class",
                    RecordDeclarationSyntax => "record",
                    StructDeclarationSyntax => "struct",
                    InterfaceDeclarationSyntax => "interface",
                    EnumDeclarationSyntax => "enum",
                    _ => "type",
                };

                foreach (string projectName in owners)
                {
                    string id = $"{projectName}:{(ns.Length == 0 ? name : ns + "." + name)}";
                    if (!typeParts.TryGetValue(id, out var part))
                    {
                        part = new TypeParts(id, name, kind, ns, projectName, Roles.For(name, rel));
                        typeParts[id] = part;
                    }
                    part.Add(rel, decl.SpanStart, NormaliseDeclaration(decl.ToString()));

                    foreach (string ident in decl.DescendantNodes()
                                 .OfType<IdentifierNameSyntax>()
                                 .Select(n => n.Identifier.ValueText)
                                 .Distinct(StringComparer.Ordinal))
                        if (ident != name)
                            mentionsRaw.Add((id, ident));

                    foreach (var (ext, source) in FileFormats.For(name, decl, usings))
                        formatsRaw.Add((id, ext, source));
                }
            }

            foreach (string projectName in owners)
            {
                var prev = fileCounts.TryGetValue(projectName, out var c) ? c : (0, 0);
                fileCounts[projectName] = (prev.Item1 + 1, prev.Item2 + lines);
            }
        }

        // A partial type is one type: all parts are merged by stable path/span order, and File lists
        // every source part so the model does not depend on filesystem enumeration order.
        var types = typeParts.Values
            .Select(t =>
            {
                var declaration = t.Declaration();
                declarations[t.Id] = declaration;
                return t.ToArchType();
            })
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        // ---- resolve mentions by NAME, and say how often that could not be done ----------------
        var byName = types
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Id).Distinct(StringComparer.Ordinal).ToList(),
                          StringComparer.Ordinal);

        // A PAIR, NOT A JOINED STRING. This was a SortedSet of "from<space>to" that got split back
        // apart downstream, and the space in one of the two literals arrived as U+0000 — a legal
        // char literal, so it compiled clean and threw IndexOutOfRange at runtime instead, in a
        // place unrelated to the line that was wrong. Nothing to join means nothing to mis-join.
        var mentions = new HashSet<(string From, string To)>();
        int ambiguous = 0;
        foreach (var (from, toName) in mentionsRaw)
        {
            if (!byName.TryGetValue(toName, out var candidates)) continue;
            if (candidates.Count > 1) { ambiguous++; continue; }   // honest: not guessed at
            if (candidates[0] == from) continue;
            mentions.Add((from, candidates[0]));
        }

        var projectsOut = projects
            .Select(p => p with
            {
                Files = fileCounts.TryGetValue(p.Name, out var counted) ? counted.Files : 0,
                Lines = fileCounts.TryGetValue(p.Name, out var counted2) ? counted2.Lines : 0,
            })
            .OrderBy(p => p.Cluster, StringComparer.Ordinal)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var typeIds = types.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        return new ArchModel(
            Schema: 1,
            Projects: projectsOut,
            Types: types,
            Mentions: mentions
                .OrderBy(m => m.From, StringComparer.Ordinal).ThenBy(m => m.To, StringComparer.Ordinal)
                .Select(m => new ArchEdge(m.From, m.To))
                .ToList(),
            Formats: BuildFormats(formatsRaw, typeIds, types),
            Externals: externalHits
                .Select(kv => new ArchExternal(kv.Key, ExternalSystems.KindOf(kv.Key), kv.Value.ToList()))
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .ToList(),
            Verbs: verbs
                .DistinctBy(v => (v.Verb, v.Project))
                .OrderBy(v => v.Project, StringComparer.Ordinal).ThenBy(v => v.Verb, StringComparer.Ordinal)
                .ToList(),
            Duplicates: Duplicates(types, declarations),
            Orphans: Orphans(types, mentions),
            Cycles: Cycles(projectsOut),
            Graphs: GraphBuilder.Build(
                projectsOut,
                types,
                mentions,
                BuildFormats(formatsRaw, typeIds, types),
                externalHits
                    .Select(kv => new ArchExternal(kv.Key, ExternalSystems.KindOf(kv.Key), kv.Value.ToList()))
                    .OrderBy(e => e.Name, StringComparer.Ordinal)
                    .ToList(),
                Duplicates(types, declarations)),
            Scripts: ScriptInventory.Collect(root),
            Stats: new ArchStats(totalFiles, totalLines, ambiguous));
    }

    /// <summary>Every .cs in the repository, with the filter pushed to the filesystem.
    ///
    /// `EnumerateFiles(root, "*.cs", AllDirectories)` lets the OS do the matching. The `"*.*"`
    /// plus `.Where()` shape enumerates every file on the volume, which on this repo means every
    /// byte under 62 bin/ and obj/ trees.</summary>
    private static IEnumerable<string> EnumerateSources(string root)
    {
        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(p => Rel(root, p), StringComparer.Ordinal))
        {
            string rel = Rel(root, path);
            // THE INSTRUMENT DOES NOT MAP ITSELF. Left in, the marker table below matches its own
            // source and the diagram grows four external systems this repo does not talk to — Visio,
            // Excel and Revit appeared as dependencies whose only evidence was this file listing
            // their names.
            if (IsArchitectureToolPath(rel)) continue;

            if (SkipSource(rel))
                continue;
            yield return path;
        }
    }

    private static List<ArchProject> ReadProjects(string root)
    {
        var result = new List<ArchProject>();
        foreach (string path in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                     .OrderBy(p => Rel(root, p), StringComparer.Ordinal))
        {
            string rel = Rel(root, path);
            if (IsArchitectureToolPath(rel) ||
                rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                continue;

            XDocument doc;
            try { doc = XDocument.Load(path); }
            catch (System.Xml.XmlException) { continue; }

            string name = Path.GetFileNameWithoutExtension(path);
            string dir = Rel(root, Path.GetDirectoryName(path)!);

            string frameworks = doc.Descendants()
                .Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                .Select(e => e.Value.Trim())
                .FirstOrDefault() ?? "";

            var refs = doc.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();

            var packages = doc.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();

            result.Add(new ArchProject(name, dir, Clusters.For(name, dir), frameworks, refs, packages, 0, 0));
        }
        return result;
    }

    private sealed class TypeParts
    {
        private readonly SortedSet<string> _files = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, List<string>> _declarations = new(StringComparer.Ordinal);

        public TypeParts(string id, string name, string kind, string ns, string project, string role)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Namespace = ns;
            Project = project;
            Role = role;
        }

        public string Id { get; }
        public string Name { get; }
        public string Kind { get; }
        public string Namespace { get; }
        public string Project { get; }
        public string Role { get; }

        public void Add(string rel, int spanStart, List<string> declaration)
        {
            _files.Add(rel);
            _declarations[$"{rel}#{spanStart.ToString(CultureInfo.InvariantCulture)}"] = declaration;
        }

        public List<string> Declaration()
        {
            var lines = new List<string>();
            foreach (var part in _declarations.Values)
            {
                if (lines.Count > 0) lines.Add("");
                lines.AddRange(part);
            }
            return lines;
        }

        public ArchType ToArchType()
            => new(Id, Name, Kind, Namespace, Project, string.Join("; ", _files), Role);
    }

    private static List<ArchFormat> BuildFormats(
        List<(string TypeId, string Ext, string Source)> formatsRaw,
        HashSet<string> typeIds,
        List<ArchType> types)
    {
        var roleByType = types.ToDictionary(t => t.Id, t => t.Role, StringComparer.Ordinal);
        return formatsRaw
            .Where(f => typeIds.Contains(f.TypeId))
            .Select(f => new ArchFormat(
                f.TypeId,
                f.Ext,
                Roles.DirectionFor(roleByType[f.TypeId]),
                f.Source))
            .DistinctBy(f => (f.Type, f.Ext))
            .OrderBy(f => f.Type, StringComparer.Ordinal).ThenBy(f => f.Ext, StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, SortedSet<string>> CompileOwners(
        string root, IReadOnlyList<ArchProject> projects, IReadOnlyList<ArchProject> byDir)
    {
        var owners = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in EnumerateSources(root))
        {
            string rel = Rel(root, file);
            var owner = byDir.FirstOrDefault(p =>
                rel.StartsWith(p.Dir + "/", StringComparison.OrdinalIgnoreCase));
            AddOwner(rel, owner?.Name ?? "(loose)");
        }

        foreach (var project in projects)
        {
            // Full compile ownership without MSBuildWorkspace: default physical ownership plus
            // explicit non-glob Compile Include links, so one source file can belong to many projects.
            string projectPath = Path.Combine(root, project.Dir, project.Name + ".csproj");
            XDocument doc;
            try { doc = XDocument.Load(projectPath); }
            catch (IOException) { continue; }
            catch (System.Xml.XmlException) { continue; }

            foreach (string include in doc.Descendants()
                         .Where(e => e.Name.LocalName == "Compile")
                         .Select(e => (string?)e.Attribute("Include"))
                         .Where(v => !string.IsNullOrWhiteSpace(v))
                         .Select(v => v!.Replace('\\', '/')))
            {
                if (include.Contains('*')) continue;

                string full = Path.GetFullPath(Path.Combine(root, project.Dir, include));
                if (!File.Exists(full)) continue;

                string rel = Rel(root, full);
                if (IsArchitectureToolPath(rel) || SkipSource(rel)) continue;
                AddOwner(rel, project.Name);
            }
        }

        return owners;

        void AddOwner(string rel, string projectName)
        {
            if (!owners.TryGetValue(rel, out var set))
                owners[rel] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(projectName);
        }
    }

    private static bool SkipSource(string rel) => SourceConventions.SkipSource(rel);

    // Shared with the staleness gate via SourceConventions — see that file.
    private static bool IsArchitectureToolPath(string rel)
        => SourceConventions.IsArchitectureToolPath(rel);

    /// <summary>Reads `args[0]` comparisons from syntax rather than grepping string literals.</summary>
    private static IEnumerable<string> CliVerbs(SyntaxNode root)
    {
        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not MemberAccessExpressionSyntax member) continue;
            if (member.Name.Identifier.ValueText != "Equals") continue;

            var first = inv.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (IsArgsZero(member.Expression) &&
                first is LiteralExpressionSyntax verb &&
                verb.IsKind(SyntaxKind.StringLiteralExpression))
                yield return verb.Token.ValueText;
            else if (member.Expression is LiteralExpressionSyntax left &&
                     left.IsKind(SyntaxKind.StringLiteralExpression) &&
                     IsArgsZero(first))
                yield return left.Token.ValueText;
        }

        foreach (var bin in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (!bin.IsKind(SyntaxKind.EqualsExpression)) continue;
            if (IsArgsZero(bin.Left) &&
                bin.Right is LiteralExpressionSyntax right &&
                right.IsKind(SyntaxKind.StringLiteralExpression))
                yield return right.Token.ValueText;
            else if (bin.Left is LiteralExpressionSyntax left &&
                     left.IsKind(SyntaxKind.StringLiteralExpression) &&
                     IsArgsZero(bin.Right))
                yield return left.Token.ValueText;
        }

        foreach (var sw in root.DescendantNodes().OfType<SwitchStatementSyntax>())
        {
            if (!IsArgsZero(sw.Expression)) continue;
            foreach (var label in sw.Sections.SelectMany(s => s.Labels).OfType<CaseSwitchLabelSyntax>())
                if (label.Value is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                    yield return lit.Token.ValueText;
        }
    }

    private static bool IsArgsZero(ExpressionSyntax? expression)
    {
        if (expression is not ElementAccessExpressionSyntax elem) return false;
        if (elem.Expression is not IdentifierNameSyntax arr || arr.Identifier.ValueText != "args") return false;
        var index = elem.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        return index is LiteralExpressionSyntax lit && lit.Token.ValueText == "0";
    }

    private static string NamespaceOf(SyntaxNode node)
    {
        var parts = new List<string>();
        for (var n = node.Parent; n is not null; n = n.Parent)
        {
            if (n is BaseNamespaceDeclarationSyntax ns) parts.Insert(0, ns.Name.ToString());
            else if (n is BaseTypeDeclarationSyntax outer) parts.Insert(0, outer.Identifier.ValueText);
        }
        return string.Join(".", parts);
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0) return 0;
        int n = 1;
        foreach (char c in text) if (c == '\n') n++;
        return n;
    }

    private static string Rel(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>One name, more than one project — and how alike the declarations actually are.</summary>
    private static List<ArchDuplicate> Duplicates(
        List<ArchType> types, Dictionary<string, List<string>> declarations)
        => types
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .Where(g => g.Select(t => t.Project).Distinct(StringComparer.Ordinal).Count() > 1)
            // MORE THAN ONE PROJECT IS NOT ENOUGH — IT MUST BE MORE THAN ONE FILE.
            //
            // Teaching the extractor about `Compile Include` links (so a shared file is owned by
            // every project that compiles it) immediately turned three deliberately-shared files
            // into 100% duplicates across two and four projects: SqlTimeouts, AppConfigKeys and
            // ConnectionStrings. Sharing one file is the OPPOSITE of duplicating it, and reporting
            // it as duplication would send someone to de-duplicate code that is already single-
            // sourced. The tell was in the data — four projects, one file.
            .Where(g => g.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g =>
            {
                var bodies = g.Select(t => t.Id)
                    .Where(declarations.ContainsKey)
                    .Select(id => declarations[id])
                    .ToList();

                double best = 0;
                for (int i = 0; i < bodies.Count; i++)
                    for (int j = i + 1; j < bodies.Count; j++)
                        best = Math.Max(best, Similarity(bodies[i], bodies[j]));

                return new ArchDuplicate(
                    g.Key,
                    g.Select(t => t.Project).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    g.Select(t => t.File).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    Math.Round(best, 3, MidpointRounding.AwayFromZero),
                    bodies.Count == 0 ? 0 : bodies.Max(b => b.Count));
            })
            .OrderByDescending(d => d.Similarity)
            .ThenByDescending(d => d.Lines)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>How alike two declarations are, 0 to 1, by longest common subsequence OF TOKENS.
    ///
    /// TOKENS, not lines, and not characters. Lines were tried first and are wrong at both ends: a
    /// one-line record whose single line differs by a field name scores ZERO, when any person would
    /// call it near-identical, and it disagreed with a character-level check on ten of thirty-eight
    /// types. Characters are right but quadratic on 15,000 of them, and a 371-line duplicate is
    /// exactly the case that matters most.
    ///
    /// A token sequence behaves like the character measure while being five times shorter. Capped at
    /// 20,000 tokens a side so the table cannot run away.</summary>
    private static double Similarity(List<string> a, List<string> b)
    {
        var ta = Tokenise(a);
        var tb = Tokenise(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;
        if (ta.Count > 20000 || tb.Count > 20000)
            return ta.SequenceEqual(tb, StringComparer.Ordinal) ? 1 : 0;

        var prev = new int[tb.Count + 1];
        var cur = new int[tb.Count + 1];
        for (int i = 1; i <= ta.Count; i++)
        {
            for (int j = 1; j <= tb.Count; j++)
                cur[j] = string.Equals(ta[i - 1], tb[j - 1], StringComparison.Ordinal)
                    ? prev[j - 1] + 1
                    : Math.Max(prev[j], cur[j - 1]);
            (prev, cur) = (cur, prev);
            Array.Clear(cur);
        }
        return 2.0 * prev[tb.Count] / (ta.Count + tb.Count);
    }

    private static List<string> Tokenise(List<string> lines)
    {
        var tokens = new List<string>();
        foreach (string line in lines)
        {
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    int start = i;
                    while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                    tokens.Add(line[start..i]);
                }
                else { tokens.Add(c.ToString()); i++; }
            }
        }
        return tokens;
    }

    /// <summary>A declaration's lines with comments and blanks removed — the comparison is about
    /// code, and one copy having kept a doc comment is not a difference worth counting.</summary>
    private static List<string> NormaliseDeclaration(string text)
    {
        var lines = new List<string>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Types nothing else names. Tests are excluded because a test is meant to be the end
    /// of a chain, and UI is excluded because XAML wires it up where a syntax read cannot see.</summary>
    private static List<ArchOrphan> Orphans(List<ArchType> types, HashSet<(string From, string To)> mentions)
    {
        var referenced = mentions.Select(m => m.To).ToHashSet(StringComparer.Ordinal);

        return types
            .Where(t => !referenced.Contains(t.Id))
            .Where(t => t.Role is not ("test" or "ui"))
            .Where(t => t.Name is not ("Program" or "App" or "MainWindow"))
            .OrderBy(t => t.Project, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new ArchOrphan(t.Id, t.Name, t.Kind, t.Project, t.File))
            .ToList();
    }

    /// <summary>Reference loops between projects, by depth-first search. There should be none, and
    /// if there are, that is the finding.</summary>
    private static List<ArchCycle> Cycles(List<ArchProject> projects)
    {
        var refs = projects.ToDictionary(p => p.Name, p => p.ProjectRefs, StringComparer.Ordinal);
        var found = new List<ArchCycle>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Walk(string node, List<string> path)
        {
            int at = path.IndexOf(node);
            if (at >= 0)
            {
                // ONE RING IS ONE CYCLE, whichever node you started from.
                //
                // The key used to be the loop's members SORTED — which fails on exactly the case it
                // was meant to handle. A loop is stored closed, so it repeats its first node at the
                // end, and a different rotation repeats a DIFFERENT node: sorting P00..P13,P00 and
                // P01..P00,P01 gives two different strings. A fourteen-project ring was therefore
                // reported fourteen times. The depth-12 cap hid it by never finding a ring that long
                // at all; removing the cap exposed a dedup that had never worked.
                //
                // Rotating to the lexically smallest member is the honest key. Rotation, not sorting:
                // A→B→C and A→C→B are different cycles over the same three projects.
                var ring = path.Skip(at).ToList();
                int lowest = ring.IndexOf(ring.Min(StringComparer.Ordinal)!);
                var canonical = ring.Skip(lowest).Concat(ring.Take(lowest)).ToList();
                string key = string.Join(">", canonical);
                if (seen.Add(key)) found.Add(new ArchCycle(canonical.Append(canonical[0]).ToList()));
                return;
            }
            if (!refs.TryGetValue(node, out var next)) return;
            path.Add(node);
            foreach (string n in next.OrderBy(x => x, StringComparer.Ordinal)) Walk(n, path);
            path.RemoveAt(path.Count - 1);
        }

        foreach (var p in projects.OrderBy(p => p.Name, StringComparer.Ordinal)) Walk(p.Name, new List<string>());
        return found;
    }
}

public static class TextFiles
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Repository text is UTF-8 unless it proves otherwise; Latin-1 fallback is stable and explicit.
    ///
    /// A BYTE-ORDER MARK IS CHECKED FIRST, because the C# compiler reads UTF-16 source happily and
    /// this did not: strict UTF-8 rejects it, Latin-1 turns it into gibberish with a NUL between
    /// every character, and Roslyn parses that as no types at all. A real compiled class disappeared
    /// from the map with no error and nothing to notice. There is no UTF-16 source in this
    /// repository today — which is exactly why it would have been found the hard way.</summary>
    public static string ReadAllText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            return Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes[3..];

        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}

// ---------------------------------------------------------------------------------------------

/// <summary>What a type DOES, from what it is called. Mechanical and checkable — the repo names
/// its parts consistently, and where it does not the role comes out "model", which is honest.</summary>
public static class Roles
{
    public static string For(string name, string relPath)
    {
        if (relPath.Contains(".Tests/", StringComparison.OrdinalIgnoreCase) ||
            relPath.Contains("Tests/", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Tests", StringComparison.Ordinal) ||
            name.EndsWith("Gate", StringComparison.Ordinal) ||
            name.EndsWith("Measurement", StringComparison.Ordinal)) return "test";

        if (EndsWithAny(name, "Reader", "Parser", "Decoder", "Loader", "Importer", "Intake")) return "read";
        if (EndsWithAny(name, "Writer", "Exporter", "Emitter", "Publisher", "Renderer")) return "write";
        if (EndsWithAny(name, "Composer", "Builder", "Generator", "Factory", "Prep")) return "compose";
        if (EndsWithAny(name, "Detector", "Classifier", "Matcher", "Resolver", "Validator", "Verifier")) return "classify";
        if (EndsWithAny(name, "Window", "View", "Dialog", "Page", "Control", "ViewModel")) return "ui";
        if (EndsWithAny(name, "Service", "Client", "Provider", "Store", "Repository", "Orchestrator", "Worker")) return "service";
        if (EndsWithAny(name, "Options", "Settings", "Constants", "Config")) return "config";
        return "model";
    }

    /// <summary>Which way a format edge points, from the role that owns it.</summary>
    public static string DirectionFor(string role) => role switch
    {
        "read" => "reads",
        "write" => "writes",
        _ => "touches",
    };

    private static bool EndsWithAny(string name, params string[] suffixes)
        => suffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal));
}

/// <summary>Which part of the business a project belongs to. Declared, because no naming rule
/// recovers it: `Kor.Opportunities.*` is BD, `Kor.Operations.EngineeringTools.*` is drawings.</summary>
public static class Clusters
{
    public static string For(string name, string dir)
    {
        if (dir.StartsWith("tools/", StringComparison.OrdinalIgnoreCase)) return "one-off tools";
        if (name.Contains("EngineeringTools", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Takeoff", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Rendering", StringComparison.OrdinalIgnoreCase)) return "drawing intake";
        if (name.Contains("Opportunit", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Bd", StringComparison.OrdinalIgnoreCase)) return "BD platform";
        if (name.Contains("Mcp", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".Ai", StringComparison.OrdinalIgnoreCase)) return "AI / MCP";
        if (name.Contains("Transmittal", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("EmailFiler", StringComparison.OrdinalIgnoreCase)) return "email + transmittals";
        if (name.Contains("App", StringComparison.OrdinalIgnoreCase)) return "desktop app";
        return "shared";
    }
}

/// <summary>Which file formats a type deals in. THE CONVERGENCE SPINE: two readers on the same
/// format are two answers to one question, and that is what the map exists to show.
///
/// Three sources, because a string literal alone finds almost nothing — on the first run exactly ONE
/// of twenty-two readers named an extension, since a reader takes a Stream and never sees a filename:
///
///   name     `DxfPlanReader` names its format. Mechanical and the strongest signal in this repo,
///            which is consistent about it.
///   using    a file that pulls in UglyToad.PdfPig reads PDF whatever it calls itself.
///   literal  an extension written out in the source.
/// </summary>
public static class FileFormats
{
    private static readonly Regex Extension = new(
        @"\.(?:dxf|dwg|e2k|f2k|edb|pdf|ifc|rvt|xlsx|xls|sco|csv|json|docx|msg|zip)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>PascalCase tokens that name a format. Ordered longest-first so `F2k` is not eaten
    /// by a shorter match.</summary>
    private static readonly (string Token, string Ext)[] NameTokens =
    {
        ("Dxf", ".dxf"), ("Dwg", ".dwg"), ("E2k", ".e2k"), ("F2k", ".f2k"), ("Edb", ".edb"),
        ("Pdf", ".pdf"), ("Ifc", ".ifc"), ("Revit", ".rvt"), ("Rvt", ".rvt"),
        ("Excel", ".xlsx"), ("Xlsx", ".xlsx"), ("Csv", ".csv"), ("Sco", ".sco"),
        ("Docx", ".docx"), ("Msg", ".msg"),
    };

    private static readonly (string Namespace, string Ext)[] LibraryNamespaces =
    {
        ("UglyToad.PdfPig", ".pdf"),
        ("ClosedXML", ".xlsx"),
        ("DocumentFormat.OpenXml", ".xlsx"),
        ("netDxf", ".dxf"),
        ("IxMilia.Dxf", ".dxf"),
    };

    public static IEnumerable<(string Ext, string Source)> For(
        string typeName, SyntaxNode decl, IReadOnlyCollection<string> usings)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (token, ext) in NameTokens)
            if (typeName.Contains(token, StringComparison.Ordinal))
                seen.TryAdd(ext, "name");

        foreach (var (ns, ext) in LibraryNamespaces)
            if (usings.Any(u => u.StartsWith(ns, StringComparison.Ordinal)))
                seen.TryAdd(ext, "using");

        foreach (var lit in decl.DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken)))
            foreach (Match m in Extension.Matches(lit.ValueText))
                seen.TryAdd(m.Value.ToLowerInvariant(), "literal");

        return seen.Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Key, StringComparer.Ordinal);
    }
}

/// <summary>Systems outside this repository that the code talks to. Declared with the marker that
/// proves it, so every entry on the diagram can be traced back to a line of code.</summary>
public static class ExternalSystems
{
    private static readonly (string Name, string Kind, string Marker)[] Known =
    {
        ("KorStandards (SQL)",      "database",   "KorStandards"),
        ("KOR-APP01\\SQLEXPRESS",   "database",   "KOR-APP01"),
        ("Deltek Vision (ODBC)",    "database",   "Deltek"),
        ("KOR-FS01 project share",  "file share", "Kor-fs01"),
        ("Microsoft Graph",         "api",        "graph.microsoft.com"),
        ("Apollo.io",               "api",        "apollo.io"),
        ("Outlook (COM)",           "desktop",    "Outlook.Application"),
        ("Visio (COM)",             "desktop",    "Visio.Application"),
        ("Excel (COM)",             "desktop",    "Excel.Application"),
        ("Revit",                   "desktop",    "Autodesk.Revit"),
        ("ETABS / SAFE",            "desktop",    "ETABSv1"),
        ("Ollama",                  "api",        "ollama"),
        ("Anthropic API",           "api",        "api.anthropic.com"),
        ("OpenAI API",              "api",        "api.openai.com"),
    };

    public static IEnumerable<(string Name, string Evidence)> Detect(string text, string relPath)
    {
        foreach (var (name, _, marker) in Known)
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                yield return (name, relPath);
    }

    public static string KindOf(string name)
        => Known.FirstOrDefault(k => k.Name == name).Kind ?? "external";
}
