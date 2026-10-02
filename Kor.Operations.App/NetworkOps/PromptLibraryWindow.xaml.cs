#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.NetworkOps.Core.Learning;
using Serilog;

namespace Kor.Operations.App.NetworkOps;

/// <summary>One line in the list on the left: a tool, a device, or one open finding under its device.</summary>
public sealed record PromptSubjectRow(string Label, string Detail, PromptRequest Request, bool IsChild, string Search)
{
    public Thickness Indent => IsChild ? new Thickness(18, 2, 2, 4) : new Thickness(2, 6, 2, 2);
    public FontWeight Weight => IsChild ? FontWeights.Normal : FontWeights.SemiBold;

    public static IReadOnlyList<PromptSubjectRow> From(PromptCatalog c)
    {
        var rows = new List<PromptSubjectRow>();
        foreach (var t in c.Tools)
            rows.Add(new($"Tool: {t.Title}", t.Summary, new PromptRequest("tool", t.Id, null, null), false, $"{t.Id} {t.Title}"));
        foreach (var d in c.Devices)
        {
            rows.Add(new(d.Device, d.Findings.Count == 0 ? $"{d.Kind} · nothing open" : $"{d.Kind} · {d.Findings.Count} open", new PromptRequest("device", null, d.DeviceId, null), false, d.Device));
            foreach (var f in d.Findings)
                rows.Add(new(f.Title, $"{f.Severity} · {f.RuleKey}", new PromptRequest("finding", null, d.DeviceId, f.FindingId), true, $"{d.Device} {f.Title} {f.RuleKey}"));
        }
        return rows;
    }
}

/// <summary>A session as the runs grid shows it.</summary>
public sealed record PromptRunView(PromptRunRow Run)
{
    public string OpenedText => Run.CreatedUtc.ToLocalTime().ToString("MMM d HH:mm");
    public string Subject => Run.Subject;
    public string By => Run.CreatedBy.Split('@')[0];
    public string OutcomeText => Run.Outcome ?? "no report yet";
    public string Summary => Run.Question is { } q ? $"Asked: {q}{(Run.Summary is { } s ? $"\n{s}" : "")}" : Run.Summary ?? "";
    public string LearnedText => string.Join("\n", new[]
    {
        Run.LearnedText is { } l ? $"Learned ({Run.LearnedStatus}): {l}" : null,
        Run.CardTitle is { } c ? $"Knowledge card ({Run.LearnedStatus}): {c}" : null,
    }.Where(x => x is not null));
    public bool AwaitsDecision => Run.LearnedStatus == "proposed";
}

public partial class PromptLibraryWindow : Window
{
    private static readonly ILogger Log = Serilog.Log.ForContext<PromptLibraryWindow>();
    private readonly NetworkOpsClient _client;
    private readonly PromptRequest? _preselect;
    private readonly CancellationTokenSource _cts = new();
    private IReadOnlyList<PromptSubjectRow> _all = [];

    /// <param name="preselect">Opened from a device or a finding: that line is selected.</param>
    public PromptLibraryWindow(NetworkOpsClient client, PromptRequest? preselect = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _preselect = preselect;
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await Guard(LoadAsync).ConfigureAwait(true);

    private async Task LoadAsync(CancellationToken ct)
    {
        Status("Reading the catalog…");
        Apply(await _client.GetPromptCatalogAsync(ct).ConfigureAwait(true), await _client.GetPromptRunsAsync(ct).ConfigureAwait(true));
        Status("");
    }

    /// <summary>Fills the window from a catalog and the runs (public so a render test can fill it without a service).</summary>
    public void Apply(PromptCatalog catalog, IReadOnlyList<PromptRunRow> runs, RenderedPrompt? shown = null)
    {
        ReportingText.Text = catalog.Reporting
            ? "Every prompt is written from NetworkOps' live database when you open it, and ends with how the session reports back. What a session proposes NetworkOps should learn waits below for your decision."
            : "Every prompt is written from NetworkOps' live database when you open it. Reporting back is not switched on yet (run db/KorNetworkOps/007_PromptLibrary.sql): sessions are told to leave a note on the device instead.";
        _all = PromptSubjectRow.From(catalog);
        ApplyFilter();
        if (_preselect is { } p)
            SubjectList.SelectedItem = _all.FirstOrDefault(r => r.Request == p);
        SubjectList.ScrollIntoView(SubjectList.SelectedItem ?? _all.FirstOrDefault());
        ApplyRuns(runs);
        if (shown is not null) { PromptTitle.Text = shown.Title; PromptText.Text = shown.Markdown; }
    }

    private void ApplyRuns(IReadOnlyList<PromptRunRow> runs) => RunsGrid.ItemsSource = runs.Select(r => new PromptRunView(r)).ToList();

    private async Task LoadRunsAsync(CancellationToken ct) => ApplyRuns(await _client.GetPromptRunsAsync(ct).ConfigureAwait(true));

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = FilterBox.Text.Trim();
        SubjectList.ItemsSource = q.Length == 0 ? _all : _all.Where(r => r.Search.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void SubjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenBtn.IsEnabled = CopyBtn.IsEnabled = SaveBtn.IsEnabled = SubjectList.SelectedItem is PromptSubjectRow;
        ShowAskScope();
    }

    // ---- Ask Claude: the person's own question, about the PC chosen on the left or the whole network.

    /// <summary>The machine an ask is about: the chosen device (or the device of a chosen finding), unless "whole network" is ticked.</summary>
    private (int? DeviceId, string Label) AskTarget()
    {
        if (AskNetworkBox.IsChecked != true && SubjectList.SelectedItem is PromptSubjectRow { Request.DeviceId: { } id } row)
            return (id, row.IsChild ? _all.LastOrDefault(r => !r.IsChild && r.Request.DeviceId == id)?.Label ?? row.Label : row.Label);
        return (null, "the whole network");
    }

    private void ShowAskScope()
    {
        var (id, label) = AskTarget();
        AskScope.Text = id is null
            ? "About the whole network. Choose a PC below to ask about it."
            : $"About {label}: the prompt carries everything NetworkOps knows about it, and what KOR has learned that applies to it.";
    }

    private void AskBox_TextChanged(object sender, TextChangedEventArgs e) => AskBtn.IsEnabled = AskBox.Text.Trim().Length > 0;

    private void AskNetwork_Changed(object sender, RoutedEventArgs e) => ShowAskScope();

    private async void Ask_Click(object sender, RoutedEventArgs e) => await Guard(async ct =>
    {
        var (id, label) = AskTarget();
        if (await RenderAsync(new PromptRequest("ask", null, id, null, AskBox.Text.Trim()), $"a question about {label}", ct).ConfigureAwait(true) is not { } prompt) return;
        Launch(prompt);
    }).ConfigureAwait(true);

    private async void SubjectList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => await Guard(ct => RenderAndAsync(ct, open: true)).ConfigureAwait(true);

    private async void Open_Click(object sender, RoutedEventArgs e) => await Guard(ct => RenderAndAsync(ct, open: true)).ConfigureAwait(true);

    private async void Save_Click(object sender, RoutedEventArgs e) => await Guard(ct => RenderAndAsync(ct, open: false)).ConfigureAwait(true);

    private async void Copy_Click(object sender, RoutedEventArgs e) => await Guard(async ct =>
    {
        if (await RenderAsync(ct).ConfigureAwait(true) is not { } prompt) return;
        Clipboard.SetText(prompt.Markdown);
        Log.Information("Prompt copied: {Title}, run {RunId}", prompt.Title, prompt.RunId);
        Status($"Copied: {prompt.Title}{RunNote(prompt)}");
    }).ConfigureAwait(true);

    private async Task RenderAndAsync(CancellationToken ct, bool open)
    {
        if (await RenderAsync(ct).ConfigureAwait(true) is not { } prompt) return;
        Launch(prompt, open);
    }

    private void Launch(RenderedPrompt prompt, bool open = true)
    {
        var path = PromptLauncher.Save(prompt);
        if (!open)
        {
            Log.Information("Prompt saved: {Title}, run {RunId}, at {Path}", prompt.Title, prompt.RunId, path);
            Status($"Saved to {path}{RunNote(prompt)}");
            return;
        }
        try
        {
            PromptLauncher.OpenInClaude(path);
            Log.Information("Prompt opened in Claude: {Title}, run {RunId}, at {Path}", prompt.Title, prompt.RunId, path);
            Status($"Claude is starting in Windows Terminal on {path}{RunNote(prompt)}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warning(ex, "Windows Terminal would not start for {Title}, run {RunId}", prompt.Title, prompt.RunId);
            Status($"Saved to {path}, but Windows Terminal would not start ({ex.Message}). Open a terminal in the repo and run: claude \"Read {path} and follow it.\"");
        }
    }

    private Task<RenderedPrompt?> RenderAsync(CancellationToken ct)
        => SubjectList.SelectedItem is PromptSubjectRow row ? RenderAsync(row.Request, row.Label, ct) : Task.FromResult<RenderedPrompt?>(null);

    private async Task<RenderedPrompt?> RenderAsync(PromptRequest request, string label, CancellationToken ct)
    {
        Status($"Writing the prompt for {label} from the live database…");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var prompt = await _client.RenderPromptAsync(request, ct).ConfigureAwait(true);
        Log.Information("Prompt written for {Subject}: run {RunId}, {Chars} characters, {Ms} ms", label, prompt.RunId, prompt.Markdown.Length, clock.ElapsedMilliseconds);
        PromptTitle.Text = prompt.Title;
        PromptText.Text = prompt.Markdown;
        PromptText.ScrollToHome();
        await LoadRunsAsync(ct).ConfigureAwait(true);
        return prompt;
    }

    private static string RunNote(RenderedPrompt p) => p.RunId is { } id ? $" · run {id}: its outcome will come back here." : "";

    private void RunsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => AcceptBtn.IsEnabled = RejectBtn.IsEnabled = RunsGrid.SelectedItem is PromptRunView { AwaitsDecision: true };

    private async void Accept_Click(object sender, RoutedEventArgs e) => await Decide(true).ConfigureAwait(true);

    private async void Reject_Click(object sender, RoutedEventArgs e) => await Decide(false).ConfigureAwait(true);

    private async Task Decide(bool accept) => await Guard(async ct =>
    {
        if (RunsGrid.SelectedItem is not PromptRunView { AwaitsDecision: true } run) return;
        await _client.DecideLearnedAsync(run.Run.RunId, accept, ct).ConfigureAwait(true);
        Log.Information("Learning from run {RunId} {Decision}", run.Run.RunId, accept ? "accepted" : "rejected");
        await LoadRunsAsync(ct).ConfigureAwait(true);
        Status(accept
            ? run.Run.CardTitle is { } card
                ? $"Accepted: \"{card}\" now goes into every later prompt about a machine it applies to{(run.Run.LearnedText is null ? "" : ", and the learning into every prompt about this kind of problem")}."
                : $"Accepted: every later prompt about {run.Subject.Split(':').Last().Trim()} carries it."
            : "Rejected: it stays in the record and goes into no prompt.");
    }).ConfigureAwait(true);

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Guard(LoadAsync).ConfigureAwait(true);

    private void Status(string text) => StatusText.Text = text;

    // Every handler is async void: nothing may escape it. A cancel only ever means the window closed.
    private async Task Guard(Func<CancellationToken, Task> work)
    {
        try { await work(_cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Prompt Library action failed. {ErrorType}: {ErrorMessage}", ex.GetType().Name, ex.Message);
            Status($"Could not do that: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        base.OnClosed(e);
    }
}
