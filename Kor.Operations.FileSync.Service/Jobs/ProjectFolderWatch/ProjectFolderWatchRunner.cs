#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace Kor.Operations.FileSync.Service.Jobs.ProjectFolderWatch;

// Nightly: is every project folder where the firm expects it? See ProjectFolderAudit for the why.
//
// Read-only on the file server and the email index. Live emails AlertTo only when something NEW
// is found (a misplaced folder or a vanished indexed folder not reported before) and remembers
// what it reported in StateDir\state.json. Shadow never emails and never updates the state; it
// writes the full findings to StateDir\shadow-<stamp>.txt.
internal sealed class ProjectFolderWatchRunner : IJobRunner
{
    public const string Name = "ProjectFolderWatch";

    private readonly IControlPlaneStore _store;
    private readonly GraphServiceClient _graph;
    private readonly FileSyncOptions _fsOpts;
    private readonly ILogger<ProjectFolderWatchRunner> _logger;

    public ProjectFolderWatchRunner(
        IControlPlaneStore store,
        GraphServiceClient graph,
        IOptions<FileSyncOptions> fsOpts,
        ILogger<ProjectFolderWatchRunner> logger)
    {
        _store = store;
        _graph = graph;
        _fsOpts = fsOpts.Value;
        _logger = logger;
    }

    public string JobName => Name;

    public async Task<JobRunResult> RunAsync(JobConfig config, string triggerSource, string? args, CancellationToken ct)
    {
        var opts = ProjectFolderWatchOptions.FromKnobs(await _store.GetKnobsAsync(Name, ct).ConfigureAwait(false));
        var isShadow = string.Equals(config.Mode, "Shadow", StringComparison.OrdinalIgnoreCase);

        if (!Directory.Exists(opts.ProjectsRoot))
            return new JobRunResult(false, $"Projects root not reachable: {opts.ProjectsRoot}");

        // 1) Misplaced: a project folder directly inside another project folder.
        var projects = new List<(string, IEnumerable<string>)>();
        foreach (var category in Directory.EnumerateDirectories(opts.ProjectsRoot))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var project in Directory.EnumerateDirectories(category))
            {
                if (!ProjectFolderAudit.TryGetProjectNumber(Path.GetFileName(project), out _, out _))
                    continue;
                var children = Directory.EnumerateDirectories(project).Select(Path.GetFileName).OfType<string>().ToList();
                projects.Add((project, children));
            }
        }

        var nested = ProjectFolderAudit.FindNested(projects, opts.IncludeSameJobNumber);

        // 2) Vanished: a folder the email index files into that no longer exists.
        var indexedFolders = await LoadIndexedFoldersAsync(opts, ct).ConfigureAwait(false);
        var vanished = indexedFolders.Where(f => !Directory.Exists(f)).ToList();

        var state = LoadState(opts.StateDir);
        var (newNested, openNested) = ProjectFolderAudit.Diff(nested.Select(n => n.NestedFolder), state.Nested);
        var (newVanished, openVanished) = ProjectFolderAudit.Diff(vanished, state.Vanished);

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "{0} project folder(s) checked; misplaced inside another project: {1} ({2} new); indexed folders gone: {3} of {4} ({5} new).",
            projects.Count, nested.Count, newNested.Count, vanished.Count, indexedFolders.Count, newVanished.Count);

        var body = BuildReport(nested, vanished, newNested, newVanished, openNested, openVanished);
        Directory.CreateDirectory(opts.StateDir);

        if (isShadow)
        {
            var path = Path.Combine(opts.StateDir, $"shadow-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(path, summary + Environment.NewLine + Environment.NewLine + body, ct).ConfigureAwait(false);
            return new JobRunResult(true, "Shadow: " + summary + " Report: " + path);
        }

        if (newNested.Count + newVanished.Count > 0)
        {
            var subject = $"[FileSync] {newNested.Count + newVanished.Count} project folder(s) not where they belong";
            await SendMailAsync(_fsOpts.AlertFromAddress, opts.AlertTo, subject, summary + "\n\n" + body, ct).ConfigureAwait(false);
            summary += $" Emailed {opts.AlertTo}.";
        }

        // Remember what is currently true, so tomorrow only reports what changed.
        SaveState(opts.StateDir, new WatchState(
            nested.Select(n => n.NestedFolder).ToList(),
            vanished));

        return new JobRunResult(true, summary);
    }

    private async Task<List<string>> LoadIndexedFoldersAsync(ProjectFolderWatchOptions opts, CancellationToken ct)
    {
        var cs = new SqlConnectionStringBuilder(_fsOpts.KorTransmittalsDb) { InitialCatalog = opts.EmailIndexDatabase }.ConnectionString;
        var folders = new List<string>();
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "SELECT DISTINCT LEFT(FilePath, CHARINDEX('\\Newforma\\', FilePath) - 1) FROM dbo.Emails WHERE CHARINDEX('\\Newforma\\', FilePath) > 0",
            cn) { CommandTimeout = 600 };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            folders.Add(r.GetString(0));
        return folders;
    }

    private static string BuildReport(
        IReadOnlyList<NestedProjectFolder> nested,
        IReadOnlyList<string> vanished,
        IReadOnlyList<string> newNested,
        IReadOnlyList<string> newVanished,
        int openNested,
        int openVanished)
    {
        var sb = new StringBuilder();
        var newSet = new HashSet<string>(newNested, StringComparer.OrdinalIgnoreCase);
        sb.AppendLine("PROJECT FOLDERS INSIDE ANOTHER PROJECT (probably dragged there by accident):");
        foreach (var n in nested.Where(n => newSet.Contains(n.NestedFolder)))
            sb.AppendLine("  NEW  " + n.NestedFolder);
        if (openNested > 0) sb.AppendLine($"  ...and {openNested} reported before, still there.");
        if (nested.Count == 0) sb.AppendLine("  none");
        sb.AppendLine();
        sb.AppendLine("PROJECT FOLDERS THE EMAIL INDEX FILES INTO THAT NO LONGER EXIST (moved, renamed or deleted):");
        foreach (var v in newVanished)
            sb.AppendLine("  NEW  " + v);
        if (openVanished > 0) sb.AppendLine($"  ...and {openVanished} reported before, still missing.");
        if (vanished.Count == 0) sb.AppendLine("  none");
        sb.AppendLine();
        sb.AppendLine("A misplaced folder: move it back to its category folder. A vanished one: search the share for its project number.");
        return sb.ToString();
    }

    private sealed record WatchState(List<string> Nested, List<string> Vanished);

    private static WatchState LoadState(string dir)
    {
        var path = Path.Combine(dir, "state.json");
        if (!File.Exists(path)) return new WatchState(new List<string>(), new List<string>());
        return JsonSerializer.Deserialize<WatchState>(File.ReadAllText(path)) ?? new WatchState(new List<string>(), new List<string>());
    }

    private static void SaveState(string dir, WatchState state)
    {
        var path = Path.Combine(dir, "state.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }

    private async Task SendMailAsync(string from, string to, string subject, string body, CancellationToken ct)
    {
        var request = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = subject,
                Body = new ItemBody { ContentType = BodyType.Text, Content = body },
                ToRecipients = new List<Recipient> { new() { EmailAddress = new EmailAddress { Address = to } } },
            },
            SaveToSentItems = false,
        };
        await _graph.Users[from].SendMail.PostAsync(request, cancellationToken: ct).ConfigureAwait(false);
    }
}
