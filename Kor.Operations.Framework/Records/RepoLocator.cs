using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Records;

/// <summary>
/// Finds a KOR repo on this machine by the leaf name a record cites (brain.Memory.SourceRepo): the folder itself when
/// <c>fromDir</c> is inside it, else the sibling folder <c>&lt;parent of the current top level&gt;\&lt;leaf&gt;</c> - accepted
/// only if git reports that folder's own top level with the same leaf. Anything else is null, which the caller reads as
/// "unverifiable", never "current" (KNOWLEDGE-IDE-SPEC-K5-K6.md, shared plumbing).
/// Not covered: a repo checked out anywhere but beside the current one (a worktree elsewhere is not found).
/// </summary>
public static class RepoLocator
{
    /// <summary>The repo's top-level folder (as git prints it, forward slashes), or null.</summary>
    public static string? Find(string repoLeaf, string? fromDir = null)
    {
        if (string.IsNullOrWhiteSpace(repoLeaf) || repoLeaf.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || repoLeaf is "." or "..")
            return null;
        var top = Git.TopLevel(fromDir ?? Environment.CurrentDirectory);
        if (top is null) return null;
        if (string.Equals(Git.Leaf(top), repoLeaf, StringComparison.Ordinal)) return top;
        var parent = Path.GetDirectoryName(top.TrimEnd('/', '\\'));
        if (parent is null) return null;
        var candidate = Path.Combine(parent, repoLeaf);
        if (!Directory.Exists(candidate)) return null;
        var candidateTop = Git.TopLevel(candidate);
        return candidateTop is not null && string.Equals(Git.Leaf(candidateTop), repoLeaf, StringComparison.Ordinal)
            ? candidateTop
            : null;
    }
}
