#nullable enable

using Kor.Operations.EngineeringTools.Dxf;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Where the live jobs the gates run against are, resolved the way the publisher resolves them:
/// one projects root, a job by its number, a drawing set or a file by its name.
/// </summary>
/// <remarks>
/// ⚠ NOTHING HERE IS A PATH. Nine test files carried the share's UNC root and the job folders'
/// full display names as string constants, and when the generated 31168 artefacts were moved one
/// folder down into "01 ETABS Models\TEST" on 2026-09-07, eight of the nine went green by
/// skipping — "share unreachable" — while the share was reachable and the set was one folder
/// away. A gate that passes by not running is the fault it exists to catch.
///
/// So: the root comes from <see cref="PublishDiscovery.ProjectsRoot"/> (the environment's answer,
/// else the share); the job is found by NUMBER; the set or file is found by NAME under the job's
/// engineering folders, as deep as the publisher itself looks and then the job tree. There are
/// exactly two outcomes. The share is unreachable, and the caller skips and SAYS so. Or the
/// share is reachable and the name is not found, and the test FAILS with where it looked,
/// because the fixture was measured against that set and somebody has to go and find it.
///
/// WHAT THIS DOES NOT COVER: it cannot tell a moved set from a renamed one, and a set that was
/// renamed to the same name as an older one would be found and read. It says where a thing is;
/// what the thing contains is the gate's own business.
/// </remarks>
internal static class LiveProjects
{
    private static readonly Dictionary<string, string> Found = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The projects root: the environment's, else the share.</summary>
    public static string Root => PublishDiscovery.ProjectsRoot;

    /// <summary>Whether the root can be listed at all. False means skip, loudly; never means pass.</summary>
    public static bool ShareReachable => Directory.Exists(Root);

    /// <summary>The job folder whose name starts with this number.</summary>
    public static string Job(string number)
    {
        lock (Found)
        {
            if (Found.TryGetValue("job:" + number, out var had)) return had;
            string job = PublishDiscovery.FindJobFolder(number, Root);
            Found["job:" + number] = job;
            return job;
        }
    }

    /// <summary>A drawing set (a folder) by name, or by a relative path such as <c>CAD Export\DXF Files\2025-06-18</c>.</summary>
    public static string Drawings(string jobNumber, string name) => Under(jobNumber, name, file: false);

    /// <summary>A file by name, such as a reference model or a published one.</summary>
    public static string File(string jobNumber, string name) => Under(jobNumber, name, file: true);

    /// <summary>A folder by name or relative path — the same lookup as <see cref="Drawings"/>, named for what it is.</summary>
    public static string Folder(string jobNumber, string name) => Under(jobNumber, name, file: false);

    private static string Under(string jobNumber, string name, bool file)
    {
        string key = (file ? "file:" : "dir:") + jobNumber + "|" + name;
        lock (Found)
        {
            if (Found.TryGetValue(key, out var had)) return had;
        }

        string job = Job(jobNumber);

        // where the publisher looks first: the engineering folders, then two below them
        string? engineering = PublishDiscovery.FindUnder(job, "02 Engineering", maxDepth: 1);
        string? found = null;
        if (engineering is not null)
            found = PublishDiscovery.FindUnder(engineering, name, maxDepth: 4, file);
        found ??= PublishDiscovery.FindUnder(job, name, maxDepth: 5, file);

        if (found is null)
        {
            throw new LiveProjectMissingException(
                $"The projects share is reachable and job {jobNumber} is at '{job}', but " +
                $"'{name}' is not within five folders of it{(engineering is null ? " (and it has no '02 Engineering' folder)" : "")}. " +
                "This gate was measured against that set. Find where it went and put it back, or rebaseline the gate " +
                "against the set that replaced it. It is not skipped: a gate that passes by not running is the fault it exists to catch.");
        }

        lock (Found)
        {
            Found[key] = found;
        }
        return found;
    }
}

/// <summary>The share is reachable and the thing a gate was measured against is not where it was.</summary>
internal sealed class LiveProjectMissingException(string message) : Exception(message);
