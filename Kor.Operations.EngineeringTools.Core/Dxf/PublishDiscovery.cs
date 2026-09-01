namespace Kor.Operations.EngineeringTools.Dxf;

public sealed record PublishDiscoveryRequest(
    string Project,
    string? ModelFolder = null,
    string? DxfFolder = null,
    string? Reference = null,
    string ProjectsRoot = PublishDiscovery.DefaultProjectsRoot);

public sealed record PublishDiscoveryResult(
    string Project,
    string JobFolder,
    string ModelFolder,
    string DxfFolder,
    string Reference);

public static class PublishDiscovery
{
    public const string DefaultProjectsRoot = @"\\Kor-fs01\Projects\Projects";

    public static PublishDiscoveryResult Discover(PublishDiscoveryRequest request)
    {
        string modelFolder;
        string jobFolder;

        if (request.ModelFolder is null)
        {
            modelFolder = FindModelFolder(request.Project, request.ProjectsRoot, out jobFolder);
        }
        else
        {
            modelFolder = request.ModelFolder;
            jobFolder = Directory.GetParent(modelFolder)?.FullName ?? modelFolder;
        }

        if (!Directory.Exists(modelFolder))
            throw new DirectoryNotFoundException($"Model folder not found '{modelFolder}'.");

        string dxfFolder = request.DxfFolder ?? FindDxfFolder(modelFolder);
        if (!Directory.Exists(dxfFolder))
            throw new DirectoryNotFoundException($"DXF folder not found '{dxfFolder}'.");

        string reference = ResolveReference(modelFolder, request.Reference);
        return new PublishDiscoveryResult(
            request.Project,
            jobFolder,
            Path.GetFullPath(modelFolder),
            Path.GetFullPath(dxfFolder),
            reference);
    }

    private static string FindModelFolder(string project, string projectsRoot, out string jobFolder)
    {
        if (!Directory.Exists(projectsRoot))
            throw new DirectoryNotFoundException($"Projects root not found '{projectsRoot}'.");

        var job = Directory.EnumerateDirectories(projectsRoot)
            .SelectMany(d => Directory.EnumerateDirectories(d, project + "*"))
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

    private static string FindDxfFolder(string modelFolder)
    {
        string parent = Directory.GetParent(modelFolder)?.FullName ?? modelFolder;
        var dxf = Directory.EnumerateDirectories(modelFolder, "*DXF*", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateDirectories(parent, "*DXF*", SearchOption.TopDirectoryOnly))
            .FirstOrDefault();
        if (dxf is null)
            throw new DirectoryNotFoundException($"No folder with DXF in its name under {modelFolder} or its parent.");
        return dxf;
    }

    public static string ResolveReference(string modelFolder, string? reference)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            string path = Path.IsPathRooted(reference)
                ? reference
                : Path.Combine(modelFolder, reference);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Reference model not found '{path}'.", path);
            return Path.GetFileName(path);
        }

        var candidates = Directory.EnumerateFiles(modelFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsEtabsModel)
            .Select(f => (Name: Path.GetFileName(f), Head: (Func<string>)(() => HeadLines(f, 40000))))
            .ToList();

        string? chosen = PublishPlan.ChooseReference(candidates, out string why);
        if (chosen is null)
            throw new InvalidOperationException(why);
        return chosen;
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
