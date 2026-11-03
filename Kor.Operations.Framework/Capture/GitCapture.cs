using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kor.Operations.Framework.Capture;

/// <summary>
/// K3 (Framework docs/KNOWLEDGE-IDE-PLAN.md): what a git hook saw, as a brain.Event - post-commit, post-merge,
/// post-checkout, pre-push. Module = the repo's top-level folder name. Actor = git user.name, plus the Claude session
/// (CLAUDE_CODE_BRIDGE_SESSION_ID) when one made the change. Git runs in <c>dir</c> (the hook's working directory, which
/// git sets to the work tree's top level).
/// </summary>
public static class GitCapture
{
    public static readonly string[] Hooks = { "post-commit", "post-merge", "post-checkout", "pre-push" };

    internal static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // stored in the DB, never rendered as HTML
        WriteIndented = false,
    };

    private const int MaxFiles = 500;
    private const int MaxMessage = 4000;
    private const string ZeroSha = "0000000000000000000000000000000000000000";

    public sealed record FileChange(string Path, string Status, string? Blob, string? From);

    /// <param name="hook">one of <see cref="Hooks"/>.</param>
    /// <param name="hookArgs">git's arguments to the hook.</param>
    /// <param name="stdin">pre-push only: the ref lines git writes to the hook's stdin.</param>
    /// <param name="guardExit">pre-push only: the review guard's exit code, when the shim ran it first.</param>
    /// <param name="module">override (tests); otherwise the top-level folder name.</param>
    public static BrainEvent Build(string hook, IReadOnlyList<string> hookArgs, string dir, string? stdin, int? guardExit,
                                   string? module)
    {
        var top = Git.TopLevel(dir) ?? throw new InvalidOperationException($"{dir} is not inside a git work tree");
        var repo = Git.Leaf(top)!;
        var actor = Actor(top);
        module ??= repo;
        return hook switch
        {
            "post-commit" => Commit(top, repo, module, actor),
            "post-merge" => Merge(top, repo, module, actor, hookArgs),
            "post-checkout" => Checkout(top, repo, module, actor, hookArgs),
            "pre-push" => Push(repo, module, actor, hookArgs, stdin, guardExit),
            _ => throw new ArgumentException($"unknown git hook '{hook}' (expected {string.Join(", ", Hooks)})"),
        };
    }

    internal static string Actor(string top)
    {
        var name = Git.Run(top, "config", "user.name")?.Trim();
        var session = Environment.GetEnvironmentVariable("CLAUDE_CODE_BRIDGE_SESSION_ID");
        var who = string.IsNullOrEmpty(name) ? "unknown" : name;
        return string.IsNullOrWhiteSpace(session) ? who : $"{who} (claude session {session.Trim()})";
    }

    private static BrainEvent Commit(string top, string repo, string module, string actor)
    {
        // One call for the identity: sha NUL parents NUL author NUL message.
        var log = Git.Run(top, "log", "-1", "--format=%H%x00%P%x00%an <%ae>%x00%B", "HEAD") ?? "";
        var parts = log.Split('\0');
        var sha = parts.Length > 0 ? parts[0].Trim() : null;
        var parents = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        var author = parts.Length > 2 ? parts[2].Trim() : null;
        var message = parts.Length > 3 ? parts[3].Trim() : null;
        var files = ParseDiffTree(Git.Run(top, "diff-tree", "--no-commit-id", "-r", "--root", "-M", "-z", "--no-abbrev", "HEAD"));
        var detail = new
        {
            parent = parents.FirstOrDefault(),
            parents = parents.Length > 1 ? parents : null,
            message = Cap(Redact.Text(message), MaxMessage),
            author,
            files = files.Take(MaxFiles).Select(f => new { path = f.Path, blob = f.Blob, status = f.Status, from = f.From }),
            files_total = files.Count,
        };
        return new BrainEvent(module, "commit", repo, sha, PathsRef(files), null,
                              JsonSerializer.Serialize(detail, Json), actor);
    }

    private static BrainEvent Merge(string top, string repo, string module, string actor, IReadOnlyList<string> args)
    {
        var head = Git.Run(top, "rev-parse", "HEAD")?.Trim();
        var orig = Git.Run(top, "rev-parse", "ORIG_HEAD")?.Trim();
        var files = orig is null
            ? new List<FileChange>()
            : ParseDiffTree(Git.Run(top, "diff-tree", "-r", "-M", "-z", "--no-abbrev", orig, "HEAD"));
        var subject = Git.Run(top, "log", "-1", "--format=%s", "HEAD")?.Trim();
        var detail = new
        {
            squash = args.Count > 0 ? args[0] == "1" : (bool?)null,
            orig_head = orig,
            head,
            subject = Redact.Text(subject),
            files = files.Take(MaxFiles).Select(f => new { path = f.Path, blob = f.Blob, status = f.Status, from = f.From }),
            files_total = files.Count,
        };
        return new BrainEvent(module, "merge", repo, head, PathsRef(files), null, JsonSerializer.Serialize(detail, Json), actor);
    }

    private static BrainEvent Checkout(string top, string repo, string module, string actor, IReadOnlyList<string> args)
    {
        var oldSha = args.Count > 0 ? args[0] : null;
        var newSha = args.Count > 1 ? args[1] : Git.Run(top, "rev-parse", "HEAD")?.Trim();
        var branchFlag = args.Count > 2 ? args[2] == "1" : (bool?)null;
        var branch = Git.Run(top, "rev-parse", "--abbrev-ref", "HEAD")?.Trim();
        var detail = new { old = oldSha, @new = newSha, branch_checkout = branchFlag, branch };
        var reference = branchFlag == false ? "file checkout" : $"branch {branch ?? "?"}";
        return new BrainEvent(module, "checkout", repo, newSha, reference, null, JsonSerializer.Serialize(detail, Json), actor);
    }

    private static BrainEvent Push(string repo, string module, string actor, IReadOnlyList<string> args, string? stdin,
                                   int? guardExit)
    {
        var remote = args.Count > 0 ? args[0] : null;
        var url = args.Count > 1 ? args[1] : null;
        var urlLeaf = Git.Leaf(url?.Replace('\\', '/'));
        var refs = ParsePushLines(stdin);
        var reference = $"{remote} {urlLeaf}: " + string.Join("; ", refs.Select(r =>
            r.LocalSha == ZeroSha ? $"delete {r.RemoteRef}" : $"{Short(r.LocalSha)} -> {r.RemoteRef}"));
        var first = refs.Select(r => r.LocalSha).FirstOrDefault(s => s != ZeroSha);
        var outcome = guardExit switch { null => null, 0 => "allowed", _ => "refused" };
        var detail = new
        {
            remote,
            url_leaf = urlLeaf,
            guard_exit = guardExit,
            refs = refs.Select(r => new { local_ref = r.LocalRef, local = r.LocalSha, remote_ref = r.RemoteRef, remote = r.RemoteSha }),
        };
        return new BrainEvent(module, "push", repo, first, reference, outcome, JsonSerializer.Serialize(detail, Json), actor);
    }

    public sealed record PushLine(string LocalRef, string LocalSha, string RemoteRef, string RemoteSha);

    /// <summary>git's pre-push stdin: "&lt;local ref&gt; &lt;local sha&gt; &lt;remote ref&gt; &lt;remote sha&gt;" per line.</summary>
    public static IReadOnlyList<PushLine> ParsePushLines(string? stdin)
    {
        var list = new List<PushLine>();
        foreach (var line in (stdin ?? "").Split('\n'))
        {
            var t = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 4) list.Add(new PushLine(t[0], t[1], t[2], t[3]));
        }
        return list;
    }

    /// <summary>
    /// Parses <c>git diff-tree -r -M -z --no-abbrev</c> raw output: ":oldmode newmode oldblob newblob STATUS\0path\0", a
    /// rename/copy carrying two paths (from, to). Blob = the NEW blob (null for a deletion).
    /// </summary>
    public static List<FileChange> ParseDiffTree(string? raw)
    {
        var list = new List<FileChange>();
        if (string.IsNullOrEmpty(raw)) return list;
        var tok = raw.Split('\0');
        for (var i = 0; i < tok.Length; i++)
        {
            var meta = tok[i].Trim('\n', '\r');
            if (!meta.StartsWith(':')) continue;
            var f = meta.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 5) continue;
            var status = f[4];
            var blob = BrainStore.Sha(f[3]) is { } b && b != ZeroSha ? b : null;
            if ((status.StartsWith('R') || status.StartsWith('C')) && i + 2 < tok.Length)
            {
                list.Add(new FileChange(tok[i + 2], status, blob, tok[i + 1]));
                i += 2;
            }
            else if (i + 1 < tok.Length)
            {
                list.Add(new FileChange(tok[i + 1], status, status.StartsWith('D') ? null : blob, null));
                i += 1;
            }
        }
        return list;
    }

    private static string? PathsRef(IReadOnlyList<FileChange> files)
    {
        if (files.Count == 0) return null;
        var text = string.Join(", ", files.Select(f => f.Path));
        if (text.Length <= 400) return text;
        var tail = $" ... ({files.Count} files)";
        return text[..(400 - tail.Length)] + tail;
    }

    private static string Short(string sha) => sha.Length > 10 ? sha[..10] : sha;

    private static string? Cap(string? s, int max) => s is null || s.Length <= max ? s : s[..max] + " ...";
}
