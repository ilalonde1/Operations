using System.Diagnostics;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Enroll;

/// <summary>One worktree of a repo, as <c>git worktree list --porcelain</c> gives it. <paramref name="Branch"/> is the
/// porcelain <c>branch</c> line whole (<c>refs/heads/x</c>), null = detached; <paramref name="Sha"/> null = no HEAD line.</summary>
public sealed record WorktreeCopy(string Path, string? Branch, string? Sha)
{
    public string Shown => $"{Path} [{(Branch is null ? "detached" : Branch.StartsWith("refs/heads/", StringComparison.Ordinal) ? Branch[11..] : Branch)}]";
}

/// <summary>Was a copy merged into a shared tip? <paramref name="Into"/> = the ref it is merged into; <paramref name="NotChecked"/>
/// = why it could not be read (then it is neither merged nor not merged).</summary>
public sealed record MergeRead(string? Into, string? NotChecked)
{
    public static readonly MergeRead No = new(null, null);
    public static MergeRead Merged(string into) => new(into, null);
    public static MergeRead Unread(string why) => new(null, why);
}

/// <summary>What the copies rule found. Faults in the order the line gives them: count, names, merged, not checked.</summary>
public sealed record CopiesVerdict(int Copies, List<string> Count, List<string> Names, List<string> Merged, List<string> NotChecked,
                                   List<string> DirtyMerged, List<string> RefsSkipped, IReadOnlyList<string> SharedRefs)
{
    public List<string> Faults => Count.Concat(Names).Concat(Merged).Concat(NotChecked).ToList();
}

/// <summary>
/// The copies rule of <c>kor enroll-check</c> for KOR.Drafter (Framework board row 2206): the C# port of KOR.Drafter
/// <c>tools/copies_check.py</c> (and its test), run at session start. KOR.Drafter has ONE code folder; its copies (git worktrees)
/// must each be named <c>KOR.Drafter-&lt;product part&gt;</c> directly under C:/VIsual Studio Projects (<see cref="Parts"/>, the .py's
/// PARTS exactly), there may be at most <see cref="MaxCopies"/> of them, and one whose branch is already merged into a shared
/// tip (<see cref="SharedRefs"/>; merged = HEAD an ancestor of the tip, or <c>git cherry</c> shows no '+') should be removed
/// once its session has ended.
/// <para>Differences from the .py, each decided by the Framework lead (row 2206): MaxCopies is 6, not 0 (Ian 19 Nov, #134895);
/// a copy merged into ANY shared ref counts; a merged copy with uncommitted changes (<c>status --porcelain
/// --untracked-files=no</c> not empty, or not readable - fails closed) is never a fault, because a lane that just started
/// sits clean-looking at its base and reads as merged; the fault never says "remove it now" and nothing is ever removed;
/// the whole rule runs to a deadline (the SessionStart hook's 30 s are shared with the DB session check) and a copy not
/// reached, or whose git read failed or expired, reads "not checked", never ok and never "not merged". It never fetches:
/// the shared refs are as last fetched. A shared ref that does not resolve is skipped and named. Its result is printed on
/// its own line and does not change enroll-check's exit code (the .py's exit 1 was for a manual run).</para>
/// <para>Checks: the copy count; each copy's folder (parent and leaf name); merged-ness of each copy's HEAD against every
/// shared ref that resolves; the merged copy's tracked-file cleanliness. Does NOT check: untracked files in a merged copy
/// (a lane's brand-new uncommitted files read as clean - the lead's chosen status form); whether a copy's session is still
/// running; whether the shared refs are current (no fetch); copies of any repo other than KOR.Drafter; the main folder
/// itself. Same-class fault it would miss: a lane that has only CREATED new files (untracked) on a branch still at the
/// merged base reads clean and merged, so it gets the "remove it once its session has ended" fault while its session is live.</para>
/// </summary>
public static class CopiesCheck
{
    public const string Main = "KOR.Drafter";
    public const string Home = "c:/visual studio projects";

    /// <summary>The product parts a copy may be named after - KOR.Drafter tools/copies_check.py PARTS exactly.</summary>
    public static readonly IReadOnlySet<string> Parts = new HashSet<string>(StringComparer.Ordinal)
    {
        "catalogue", "markup-intake", "arch-intake", "plan", "revit", "drafted-set", "etabs", "safe", "viewer",
        "quantities", "comparison", "wood-frame", "geometry-gates", "revit-tools", "integration",
    };

    /// <summary>The C# port's allowance (the .py had 0): Ian 19 Nov chose "2 in parallel in product-part-named copies
    /// (KOR.Drafter-&lt;part&gt;, up to ~6 at once, each removed when its branch merges)" - record #134895.</summary>
    public const int MaxCopies = 6;

    /// <summary>The shared tips a copy's branch merges into (the port merges into the second), as last fetched.</summary>
    public static readonly IReadOnlyList<string> SharedRefs = new[] { "fs01/standard-details-rendered-image-store", "fs01/port-merge-19nov" };

    /// <summary>Total time for the git reads (the SessionStart hook's 30 s are shared with the DB session check).</summary>
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(12);

    /// <summary>The most one git read may take inside the deadline. Each read's time runs from git's start (<see cref="Git.Exec"/>),
    /// so the rule can end past the deadline by one git start plus <see cref="Git.KillGrace"/>.</summary>
    public static readonly TimeSpan PerCall = TimeSpan.FromSeconds(6);

    /// <summary>A git read is not started with less time than this left (it could not finish; starting it only adds a kill).</summary>
    public static readonly TimeSpan MinSlice = TimeSpan.FromMilliseconds(250);

    /// <summary>How many git reads run at once (19 Nov, 6 port lanes running: 8 at once finished the 29 live copies in 11-12 s with
    /// 0-1 not checked over 3 runs; 4 at once left 0-21 not checked).</summary>
    public const int Parallelism = 8;

    /// <summary>The longest the --hook line may run before "... and N more".</summary>
    public const int HookLineCap = 1500;

    /// <summary>Worktrees are owned by two accounts (kor\ilalonde, kor\kor-claude); without this git refuses "dubious ownership".</summary>
    public static readonly string[] SafeDirectory = { "-c", "safe.directory=*" };

    // ---- pure -------------------------------------------------------------------------------------------------------

    /// <summary>Null if the folder is KOR.Drafter-&lt;part&gt; directly under C:/VIsual Studio Projects, else the reason.</summary>
    public static string? NameFault(string path)
    {
        var p = path.Replace('\\', '/').TrimEnd('/');
        var cut = p.LastIndexOf('/');
        var parent = cut < 0 ? "" : p[..cut];
        var leaf = cut < 0 ? p : p[(cut + 1)..];
        if (!string.Equals(parent.ToLowerInvariant(), Home, StringComparison.Ordinal))
            return $"not under C:/VIsual Studio Projects: {p}";
        if (!leaf.StartsWith(Main + "-", StringComparison.Ordinal) || !Parts.Contains(leaf[(Main.Length + 1)..]))
            return $"not a product-part name (KOR.Drafter-<{string.Join("|", Parts.OrderBy(x => x, StringComparer.Ordinal))}>): {leaf}";
        return null;
    }

    /// <summary>
    /// The faults of a repo's copies (the main folder excluded). Count and name faults first, with no git, so a merged read
    /// that runs out of time can never hide one. <paramref name="merged"/> reads one copy; <paramref name="dirty"/> is asked
    /// only of a merged copy: true = uncommitted changes, false = clean, null = not read (counted dirty: no merged fault).
    /// </summary>
    public static CopiesVerdict Faults(IReadOnlyList<WorktreeCopy> copies, Func<WorktreeCopy, MergeRead> merged, Func<WorktreeCopy, bool?> dirty,
                                       IReadOnlyList<string>? sharedRefs = null)
    {
        var v = new CopiesVerdict(copies.Count, new(), new(), new(), new(), new(), new(), sharedRefs ?? SharedRefs);
        if (copies.Count > MaxCopies) v.Count.Add($"{copies.Count} copies; at most {MaxCopies} allowed");
        foreach (var c in copies)
            if (NameFault(c.Path) is { } f) v.Names.Add(f);
        foreach (var c in copies)
        {
            var m = merged(c);
            if (m.NotChecked is { } why) { v.NotChecked.Add($"not checked: {c.Shown} ({why})"); continue; }
            if (m.Into is null) continue;
            switch (dirty(c))
            {
                case false: v.Merged.Add($"merged into {m.Into}; remove it once its session has ended: {c.Shown}"); break;
                case true: v.DirtyMerged.Add(c.Shown); break;
                case null: v.NotChecked.Add($"not checked: {c.Shown} (merged into {m.Into}, but its status could not be read)"); break;
            }
        }
        return v;
    }

    /// <summary>The verdict line: "kor copies-check: ok - ..." or "kor copies-check: FAIL - N fault(s): ...". With
    /// <paramref name="cap"/> the line stops near that length with "... and N more".</summary>
    public static string Line(CopiesVerdict v, int? cap = null)
    {
        var skipped = v.RefsSkipped.Count == 0 ? "" : $" [shared ref(s) skipped: {string.Join("; ", v.RefsSkipped)}]";
        var left = v.DirtyMerged.Count == 0 ? "" : $"; {v.DirtyMerged.Count} merged but with uncommitted changes, left alone";
        var faults = v.Faults;
        if (faults.Count == 0)
        {
            var into = v.SharedRefs.Where(r => !v.RefsSkipped.Any(s => s.StartsWith(r + " ", StringComparison.Ordinal))).ToList();
            return $"kor copies-check: ok - {v.Copies} cop{(v.Copies == 1 ? "y" : "ies")} (at most {MaxCopies}), each KOR.Drafter-<part> under " +
                   $"C:/VIsual Studio Projects, none merged into {(into.Count == 0 ? "nothing (no shared ref resolved)" : string.Join(" or ", into))}{left}{skipped}";
        }
        // the not-checked count is in the head: those faults come last, so a capped line may not show them
        var unread = v.NotChecked.Count == 0 ? "" : $" ({v.NotChecked.Count} of them not checked)";
        var head = $"kor copies-check: FAIL - {faults.Count} fault(s){unread} of {v.Copies} copies{skipped}: ";
        var line = head + string.Join("; ", faults) + left;
        if (cap is not { } max || line.Length <= max) return line;
        var sb = new System.Text.StringBuilder(head);
        for (var i = 0; i < faults.Count; i++)
        {
            var more = $"... and {faults.Count - i} more";
            var piece = (i == 0 ? "" : "; ") + faults[i];
            if (sb.Length + piece.Length + 2 + more.Length > max) return sb.Append(i == 0 ? "" : "; ").Append(more).ToString();
            sb.Append(piece);
        }
        return sb.ToString();
    }

    /// <summary>Rows of <c>git worktree list --porcelain</c>: blank-line separated, "worktree &lt;path&gt;" taken whole (paths hold spaces).</summary>
    public static List<WorktreeCopy> ParseWorktrees(string porcelain)
    {
        var rows = new List<WorktreeCopy>();
        string? path = null, branch = null, sha = null;
        foreach (var raw in porcelain.ReplaceLineEndings("\n").Split('\n').Append(""))
        {
            if (raw.Length == 0)
            {
                if (path is not null) rows.Add(new WorktreeCopy(path, branch, sha));
                path = branch = sha = null;
                continue;
            }
            var sp = raw.IndexOf(' ');
            var key = sp < 0 ? raw : raw[..sp];
            var val = sp < 0 ? "" : raw[(sp + 1)..];
            switch (key)
            {
                case "worktree": path = val; break;
                case "HEAD": sha = val; break;
                case "branch": branch = val; break;
            }
        }
        return rows;
    }

    // ---- git --------------------------------------------------------------------------------------------------------

    /// <summary>The runner (dir, timeout, args) - <see cref="Git.Exec"/> unless a test passes its own.</summary>
    public delegate Git.Result Runner(string dir, TimeSpan timeout, string[] args);

    /// <summary>
    /// Reads KOR.Drafter's copies at <paramref name="repoDir"/> and judges them. Every git call carries
    /// <c>-c safe.directory=*</c> before its subcommand and its own timeout inside the one <paramref name="deadline"/>.
    /// A merged read: a branch copy in a shared ref's <c>for-each-ref --merged</c> set (matched by its porcelain branch
    /// line) is merged; otherwise, for a detached HEAD or when that set could not be read, <c>merge-base --is-ancestor</c>
    /// (exit 1 = no), then <c>git cherry</c>. The copies' reads run <see cref="Parallelism"/> at a time. Throws when the
    /// worktree list itself cannot be read.
    /// </summary>
    public static CopiesVerdict Check(string repoDir, TimeSpan? deadline = null, Runner? git = null, IReadOnlyList<string>? sharedRefs = null)
    {
        var run = git ?? ((d, t, a) => Git.Exec(d, t, a));
        var refs = sharedRefs ?? SharedRefs;
        var limit = deadline ?? DefaultDeadline;
        var sw = Stopwatch.StartNew();
        TimeSpan Left() => limit - sw.Elapsed;
        TimeSpan Slice() => Left() < PerCall ? Left() : PerCall;
        Git.Result Git_(string dir, params string[] args) =>
            Left() < MinSlice ? new Git.Result(null, null, true) : run(dir, Slice(), SafeDirectory.Concat(args).ToArray());

        var list = Git_(repoDir, "worktree", "list", "--porcelain");
        if (!list.Ok)
            throw new InvalidOperationException($"git worktree list in {repoDir} {(list.Expired ? "ran out of time" : $"failed (exit {list.ExitCode?.ToString() ?? "none"})")}");
        var rows = ParseWorktrees(list.Stdout!);
        var copies = rows.Skip(1).ToList();   // the first row is the main folder

        // the shared tips, and the branches already in each (ancestors) - one read per ref, not per copy
        // (read side by side, kept in the listed order)
        var perRef = new (string? Skipped, (string Name, string Sha, HashSet<string>? MergedBranches)? Tip)[refs.Count];
        Parallel.For(0, refs.Count, i =>
        {
            var r = refs[i];
            var rp = Git_(repoDir, "rev-parse", "--verify", "-q", r + "^{commit}");
            if (!rp.Ok)
            {
                perRef[i] = ($"{r} {(rp.Expired ? "not read (time ran out)" : rp.ExitCode is null ? "not read (git did not run)" : "does not resolve")}", null);
                return;
            }
            var fe = Git_(repoDir, "for-each-ref", "--merged=" + rp.Stdout!.Trim(), "--format=%(refname)", "refs/heads");
            var set = fe.Ok ? fe.Stdout!.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToHashSet(StringComparer.Ordinal) : null;
            perRef[i] = (null, (r, rp.Stdout!.Trim(), set));
        });
        var skipped = perRef.Where(p => p.Skipped is not null).Select(p => p.Skipped!).ToList();
        var tips = perRef.Where(p => p.Tip is not null).Select(p => p.Tip!.Value).ToList();

        MergeRead Merged(WorktreeCopy c)
        {
            if (tips.Count == 0) return MergeRead.Unread("no shared ref resolved");
            foreach (var t in tips)
                if (c.Branch is not null && t.MergedBranches is not null && t.MergedBranches.Contains(c.Branch)) return MergeRead.Merged(t.Name);
            if (c.Sha is null) return MergeRead.Unread("no HEAD in the worktree list");
            string? unread = null;
            foreach (var t in tips)
            {
                if (Left() < MinSlice) return MergeRead.Unread("deadline reached");
                if (c.Branch is null || t.MergedBranches is null)
                {
                    var mb = Git_(repoDir, "merge-base", "--is-ancestor", c.Sha, t.Sha);
                    if (mb is { Expired: false, ExitCode: 0 }) return MergeRead.Merged(t.Name);
                    if (mb is not { Expired: false, ExitCode: 1 }) { unread = Why(mb, "merge-base"); continue; }
                }
                var ch = Git_(repoDir, "cherry", t.Sha, c.Sha);
                if (!ch.Ok) { unread = Why(ch, "cherry"); continue; }
                if (!ch.Stdout!.ReplaceLineEndings("\n").Split('\n').Any(l => l.StartsWith('+'))) return MergeRead.Merged(t.Name);
            }
            return unread is null ? MergeRead.No : MergeRead.Unread(unread);
        }

        bool? Dirty(WorktreeCopy c)
        {
            var st = Git_(c.Path, "status", "--porcelain", "--untracked-files=no");
            return st.Ok ? st.Stdout!.Trim().Length > 0 : null;
        }

        // the reads run side by side (a status took 1.9-3.3 s on a port lane, 19 Nov; one after another, 30 copies cannot fit the
        // deadline), all inside the one deadline; a merged copy's status is read by the same worker straight after its merged
        // read (no barrier between the two phases); the judging below is the pure Faults, which asks no git
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Parallelism };
        var merges = new System.Collections.Concurrent.ConcurrentDictionary<WorktreeCopy, MergeRead>();
        var dirty = new System.Collections.Concurrent.ConcurrentDictionary<WorktreeCopy, bool?>();
        Parallel.ForEach(copies.Distinct(), opts, c =>
        {
            var m = merges[c] = Merged(c);
            if (m.Into is not null) dirty[c] = Dirty(c);
        });
        return Faults(copies, c => merges[c], c => dirty.TryGetValue(c, out var d) ? d : null, refs) with { RefsSkipped = skipped };
    }

    private static string Why(Git.Result r, string verb) =>
        r.Expired ? $"git {verb} ran out of time" : $"git {verb} failed (exit {r.ExitCode?.ToString() ?? "none"})";
}
