// WHAT COUNTS AS A SOURCE FILE, AND WHOSE IT IS — DEFINED ONCE.
//
// The extractor decides which .cs files exist and which project owns each one. The staleness gate in
// Kor.Operations.App.Tests decides the same thing a second time, so it can tell when the committed
// model no longer matches the tree without paying for Roslyn on every edit.
//
// Those two answers MUST be identical. If the gate counts one file the extractor does not, it
// reports the map stale forever and gets switched off; if it misses one, it passes while the map
// rots — which is the failure it exists to prevent.
//
// They were copy-pasted, and the audit called that out as the most likely place for a real defect in
// the whole commit. It was right within the hour: tightening `IsArchitectureToolPath` in the
// extractor left the gate's copy behind, and the two disagreed immediately.
//
// So this file is SHARED BY Compile Include — the same mechanism `SqlTimeouts.cs` uses, and the one
// the map itself learned to read this week. One definition, compiled into both, with no project
// reference and no Roslyn dragged into a suite that runs on every edit.

using System;

namespace Kor.Operations.Architecture;

public static class SourceConventions
{
    private const string ToolProject = "Kor.Operations.Architecture";

    /// <summary>The mapper and its tests, and nothing else.
    ///
    /// A bare prefix match would swallow any future project whose name merely begins the same way —
    /// `Kor.Operations.ArchitectureReports` would vanish from the map and from the gate together,
    /// silently, and the gate is the thing that would otherwise have said so. The boundary has to be
    /// real: the name itself, or the name followed by a dot.</summary>
    public static bool IsArchitectureToolPath(string rel)
    {
        int slash = rel.IndexOf('/');
        string first = slash < 0 ? rel : rel[..slash];
        return first.Equals(ToolProject, StringComparison.OrdinalIgnoreCase)
            || first.StartsWith(ToolProject + ".", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Build output, vendored packages, and files a generator wrote. Paths arrive relative
    /// to the repository root with forward slashes.</summary>
    public static bool SkipSource(string rel)
        => rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
        || rel.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        || rel.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
        || rel.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
}
