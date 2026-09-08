namespace Kor.Operations.EngineeringTools.Dxf;

/// <param name="DxfFolder">
/// The drawing set to read: a full path, or the NAME of a folder beneath the model folder when the
/// job holds several sets. Null lets discovery take the one set it finds, and refuse to guess
/// between more than one.
/// </param>
/// <param name="ProjectsRoot">Null takes <see cref="PublishDiscovery.ProjectsRoot"/>.</param>
public sealed record PublishDiscoveryRequest(
    string Project,
    string? ModelFolder = null,
    string? DxfFolder = null,
    string? Reference = null,
    string? ProjectsRoot = null);

public sealed record PublishDiscoveryResult(
    string Project,
    string JobFolder,
    string ModelFolder,
    string DxfFolder,
    string Reference);

public static class PublishDiscovery
{
    public const string DefaultProjectsRoot = @"\\Kor-fs01\Projects\Projects";

    /// <summary>Overrides <see cref="DefaultProjectsRoot"/> for every publish and every live test.</summary>
    public const string ProjectsRootEnvironmentVariable = "KOR_PROJECTS_ROOT";

    /// <summary>
    /// Where the jobs are: the environment's answer, else the share. ONE place says so — the
    /// tests that gate on live jobs carried the share's UNC path in nine files of their own, and
    /// a moved folder was a silent pass in eight of them.
    /// </summary>
    public static string ProjectsRoot =>
        Environment.GetEnvironmentVariable(ProjectsRootEnvironmentVariable) is { Length: > 0 } root
            ? root
            : DefaultProjectsRoot;

    /// <summary>How deep under the model folder a drawing set or a reference model may sit.</summary>
    /// <remarks>
    /// The share's layout is a human decision and it moves: on 2026-09-07 every generated 31168
    /// artefact — three DXF sets, the reference, the published models — was found one folder down
    /// from where it had been, in "01 ETABS Models\TEST". A search one level deep read nothing and
    /// the gate that reads what ships failed on a folder it could have found.
    /// </remarks>
    public const int SearchDepth = 2;

    public static PublishDiscoveryResult Discover(PublishDiscoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string modelFolder;
        string jobFolder;

        if (request.ModelFolder is null)
        {
            modelFolder = FindModelFolder(request.Project, request.ProjectsRoot ?? ProjectsRoot, out jobFolder);
        }
        else
        {
            modelFolder = request.ModelFolder;
            jobFolder = Directory.GetParent(modelFolder)?.FullName ?? modelFolder;
        }

        if (!Directory.Exists(modelFolder))
            throw new DirectoryNotFoundException($"Model folder not found '{modelFolder}'.");

        // A REBUILT MODEL LANDS BESIDE THE REFERENCE IT WAS BUILT AGAINST. When the reference is
        // named and sits below the folder found, that folder is the model folder.
        string referencePath = ResolveReferencePath(modelFolder, request.Reference);
        modelFolder = Path.GetDirectoryName(referencePath) ?? modelFolder;

        string dxfFolder = ResolveDxfFolder(modelFolder, request.DxfFolder);
        if (!Directory.Exists(dxfFolder))
            throw new DirectoryNotFoundException($"DXF folder not found '{dxfFolder}'.");

        return new PublishDiscoveryResult(
            request.Project,
            jobFolder,
            Path.GetFullPath(modelFolder),
            Path.GetFullPath(dxfFolder),
            Path.GetFileName(referencePath));
    }

    /// <summary>The job folder whose name starts with the project number, under the projects root.</summary>
    public static string FindJobFolder(string project, string? projectsRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        projectsRoot ??= ProjectsRoot;
        if (!Directory.Exists(projectsRoot))
            throw new DirectoryNotFoundException($"Projects root not found '{projectsRoot}'.");

        var job = Directory.EnumerateDirectories(projectsRoot)
            .SelectMany(SafeChildren(project))
            .FirstOrDefault();
        return job ?? throw new DirectoryNotFoundException($"No job folder starting with '{project}' under {projectsRoot}.");
    }

    /// <summary>
    /// A folder or file with exactly this name somewhere under the root, at most
    /// <paramref name="maxDepth"/> folders down; null when there is none. Directories only are
    /// walked, guarded, so a share that refuses one bucket does not hide the rest.
    /// </summary>
    public static string? FindUnder(string root, string name, int maxDepth, bool file = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Directory.Exists(root)) return null;

        foreach (string folder in new[] { root }.Concat(EnumerateDirectories(root, maxDepth)))
        {
            string candidate = Path.Combine(folder, name);
            if (file ? File.Exists(candidate) : Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string FindModelFolder(string project, string projectsRoot, out string jobFolder)
    {
        if (!Directory.Exists(projectsRoot))
            throw new DirectoryNotFoundException($"Projects root not found '{projectsRoot}'.");

        // ONE UNREADABLE BUCKET MUST NOT HIDE A JOB IN THE NEXT ONE.
        //
        // The projects root holds a bucket per sector and the enumeration walks all of them. On a
        // share, any one can refuse: a permission this account does not hold, a folder mid-rename,
        // a reconnecting mount. Unguarded, that throws out of the whole SelectMany and the publish
        // fails before it has read a drawing -- for a condition in a bucket the job is not even in.
        //
        // The script searched each child with -ErrorAction SilentlyContinue for exactly this
        // reason. EnumerateDirectories below already guards its walk; this one did not, and the two
        // are the same problem.
        var job = Directory.EnumerateDirectories(projectsRoot)
            .SelectMany(SafeChildren(project))
            .FirstOrDefault();
        if (job is null)
            throw new DirectoryNotFoundException($"No job folder starting with '{project}' under {projectsRoot}.");

        var model = EnumerateDirectories(job, maxDepth: 3)
            .Where(d => Path.GetFileName(d).Contains("ETABS Models", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (model is null)
            throw new DirectoryNotFoundException($"Found {Path.GetFileName(job)} but no 'ETABS Models' folder inside it.");

        jobFolder = job;
        return model;
    }

    /// <summary>
    /// Job folders under one bucket, or nothing if that bucket will not be read. Materialised
    /// inside the try because enumeration is lazy and would otherwise throw at the call site.
    /// </summary>
    internal static Func<string, IEnumerable<string>> SafeChildren(string project) => bucket =>
    {
        try
        {
            return Directory.EnumerateDirectories(bucket, project + "*").ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    };

    /// <summary>
    /// Every drawing set a publish could read: a folder with DXF in its name that itself holds
    /// .dxf files, under the model folder (to <see cref="SearchDepth"/>) or beside it.
    /// </summary>
    public static IReadOnlyList<string> DxfSets(string modelFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelFolder);
        string parent = Directory.GetParent(modelFolder)?.FullName ?? modelFolder;

        var candidates = EnumerateDirectories(modelFolder, SearchDepth)
            .Concat(SafeTopLevel(parent))
            .Where(d => Path.GetFileName(d).Contains("DXF", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var sets = new List<string>();
        foreach (string folder in candidates)
        {
            try
            {
                // push the filter to the filesystem; the set is the folder that holds the sheets
                if (Directory.EnumerateFiles(folder, "*.dxf", SearchOption.TopDirectoryOnly).Any())
                    sets.Add(folder);
            }
            catch
            {
                // a folder that refuses to be read is not a set this publish can use
            }
        }
        return sets.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The one drawing set, the named one among several, or a refusal that lists them. Guessing
    /// between sets is how a model gets built from drawings nothing ships.
    /// </summary>
    private static string ResolveDxfFolder(string modelFolder, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && Path.IsPathRooted(requested))
            return requested;

        var sets = DxfSets(modelFolder);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var named = sets.FirstOrDefault(s => Path.GetFileName(s).Equals(requested, StringComparison.OrdinalIgnoreCase));
            return named ?? throw new DirectoryNotFoundException(
                $"No drawing set named '{requested}' under {modelFolder}. " +
                (sets.Count == 0 ? "No DXF set found at all." : "Found: " + string.Join(", ", sets.Select(Path.GetFileName)) + "."));
        }

        return sets.Count switch
        {
            0 => throw new DirectoryNotFoundException(
                $"No folder with DXF in its name holding .dxf files within {SearchDepth} levels of {modelFolder} or beside it."),
            1 => sets[0],
            _ => throw new InvalidOperationException(
                $"{sets.Count} drawing sets under {modelFolder}: " + string.Join(", ", sets.Select(Path.GetFileName)) +
                ". Name the one to publish from with --dxf <name>; a publish does not guess which drawings it reads."),
        };
    }

    private static IEnumerable<string> SafeTopLevel(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>The reference model's file name, resolved the way <see cref="Discover"/> resolves it.</summary>
    public static string ResolveReference(string modelFolder, string? reference)
        => Path.GetFileName(ResolveReferencePath(modelFolder, reference));

    /// <summary>
    /// The reference model's full path: the one named, found beside the model folder or up to
    /// <see cref="SearchDepth"/> below it, or the one chosen from the models the folder holds.
    /// </summary>
    public static string ResolveReferencePath(string modelFolder, string? reference)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            if (Path.IsPathRooted(reference))
            {
                if (!File.Exists(reference))
                    throw new FileNotFoundException($"Reference model not found '{reference}'.", reference);
                return reference;
            }

            string? found = FindUnder(modelFolder, reference, SearchDepth, file: true);
            return found ?? throw new FileNotFoundException(
                $"Reference model '{reference}' not found in {modelFolder} or within {SearchDepth} levels below it.",
                Path.Combine(modelFolder, reference));
        }

        var candidates = Directory.EnumerateFiles(modelFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsEtabsModel)
            .Select(f => (Name: Path.GetFileName(f), Head: (Func<string>)(() => HeadLines(f, 40000))))
            .ToList();

        string? chosen = PublishPlan.ChooseReference(candidates, out string why);
        if (chosen is null)
            throw new InvalidOperationException(why);
        return Path.Combine(modelFolder, chosen);
    }

    private static bool IsEtabsModel(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".e2k", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".$et", StringComparison.OrdinalIgnoreCase);
    }

    internal static IEnumerable<string> EnumerateDirectories(string root, int maxDepth)
    {
        var pending = new Queue<(string Folder, int Depth)>();
        pending.Enqueue((root, 0));
        while (pending.Count > 0)
        {
            var (folder, depth) = pending.Dequeue();
            if (depth >= maxDepth) continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(folder);
            }
            catch
            {
                continue;
            }

            foreach (string child in children)
            {
                yield return child;
                pending.Enqueue((child, depth + 1));
            }
        }
    }

    private static string HeadLines(string path, int lines)
    {
        using var reader = new StreamReader(path);
        var head = new List<string>();
        for (int i = 0; i < lines && !reader.EndOfStream; i++)
            head.Add(reader.ReadLine() ?? string.Empty);
        return string.Join("\n", head);
    }
}
