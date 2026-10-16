#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>A run as the Doctrine screen shows it: what was asked, when, and what came back.</summary>
public sealed record BdPromptRunView(BdPromptRunRow Run)
{
    public string Heading => Run.Kind == "synthesis" ? $"Synthesis: {Run.Subject}" : Run.Question ?? Run.Subject;
    public string When => Run.CreatedAtUtc.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture) + " · " + Run.Subject;
    public string StatusText => Run.Outcome switch
    {
        null => "Waiting for Claude's report",
        "answered" => Run.ProposedCards > 0 ? $"Answered · {Run.ProposedCards} card(s) waiting for you" : "Answered",
        "partly" => "Partly answered" + (Run.ProposedCards > 0 ? $" · {Run.ProposedCards} card(s) waiting for you" : ""),
        _ => "Not answered",
    };
    public string Answer => Run.Answer ?? "";
}

/// <summary>A prompt ready to open in Claude: its file name and text.</summary>
public sealed record BdPreparedPrompt(long RunId, string FileName, string Markdown);

/// <summary>
/// The BD consigliere: asking Claude from the Director, and the doctrine it learns from. The NetworkOps Prompt Library method —
/// compose the prompt from live data when it is opened, open Claude Code on it, take the report back by one-time token, and let
/// only cards Ian accepts reach later prompts. Nothing here sends anything to anyone (CLAUDE.md rule 14: no API call).
/// </summary>
public sealed class BdDoctrineViewModel : ObservableObject
{
    public const string Role = "BD";
    private readonly IBdDoctrineStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private IReadOnlyList<DoctrineCardRow> _cards = Array.Empty<DoctrineCardRow>();
    private string _status = "";

    public BdDoctrineViewModel(IBdDoctrineStore store) : this(store, () => DateTimeOffset.Now) { }

    public BdDoctrineViewModel(IBdDoctrineStore store, Func<DateTimeOffset> clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ObservableCollection<DoctrineCardRow> Proposed { get; } = new();
    public ObservableCollection<DoctrineCardRow> Accepted { get; } = new();
    public ObservableCollection<BdPromptRunView> Runs { get; } = new();
    public string Summary => $"{Accepted.Count} accepted · {Proposed.Count} waiting for you · {Runs.Count(r => r.Run.Outcome is null)} question(s) still with Claude";
    public string ProposedHeading => Proposed.Count == 0 ? "Nothing waiting for you" : $"Waiting for you: {Proposed.Count}";
    public bool HasProposed => Proposed.Count > 0;
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public void Say(string text) => Status = text;

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            _cards = await _store.ListCardsAsync(Role, ct).ConfigureAwait(true);
            var runs = await _store.ListRunsAsync(50, ct).ConfigureAwait(true);
            Proposed.Clear();
            foreach (var c in _cards.Where(c => c.Status == "proposed")) Proposed.Add(c);
            Accepted.Clear();
            foreach (var c in _cards.Where(c => c.Status == "accepted").OrderBy(c => c.Title, StringComparer.CurrentCulture)) Accepted.Add(c);
            Runs.Clear();
            foreach (var r in runs) Runs.Add(new BdPromptRunView(r));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not load the doctrine.");
            Status = "The doctrine could not be loaded. Refresh to try again.";
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ProposedHeading));
        OnPropertyChanged(nameof(HasProposed));
    }

    /// <summary>A synthesis started in the last two days that has not reported. Older ones are treated as abandoned.</summary>
    public static BdPromptRunRow? OpenSynthesis(IReadOnlyList<BdPromptRunRow> runs, DateTimeOffset now) =>
        runs.FirstOrDefault(r => r.Kind == "synthesis" && r.Outcome is null && r.CreatedAtUtc > now.AddDays(-2));

    /// <summary>Accepts every card still waiting, oldest first, so an amendment is decided after the card it amends.</summary>
    public async Task<int> AcceptAllAsync(string actor, CancellationToken ct)
    {
        var accepted = 0;
        foreach (var card in Proposed.OrderBy(c => c.Id).ToList())
        {
            if (await _store.DecideCardAsync(card.Id, true, actor, ct).ConfigureAwait(true)) accepted++;
        }

        Status = $"Accepted {accepted} card(s).";
        await LoadAsync(ct).ConfigureAwait(true);
        return accepted;
    }

    /// <summary>Accepts or rejects a proposed card; accepting an amendment retires the card it amends.</summary>
    public async Task<bool> DecideAsync(DoctrineCardRow card, bool accept, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(card);
        var done = await _store.DecideCardAsync(card.Id, accept, actor, ct).ConfigureAwait(true);
        Status = !done ? "That card was already decided. Refreshed."
            : accept ? $"Accepted: {card.Title}" + (card.AmendsCardId is { } a ? $" (card #{a} retired)" : "") : $"Rejected: {card.Title}";
        await LoadAsync(ct).ConfigureAwait(true);
        return done;
    }

    /// <summary>A question about today, a person or a pursuit, composed now from what the Director knows, with the doctrine that applies.</summary>
    public async Task<BdPreparedPrompt> PrepareAskAsync(string question, string subject, DoctrineContext context, IReadOnlyList<string> facts, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("Type the question first.", nameof(question));
        var accepted = _cards.Where(c => c.Status == "accepted").ToList();
        var applies = accepted.Where(c => BdDoctrine.AppliesTo(c.AppliesTo, context)).ToList();
        var library = accepted.Except(applies).ToList();
        var (token, tokenSha) = BdDoctrine.NewToken();
        var draft = new BdAskInput(question.Trim(), actor, subject, context, facts, applies, library, _clock());
        // The prompt carries its run id, so the run is recorded first and the hash of the final text stored after.
        var runId = await _store.StartRunAsync("ask", Role, subject, question.Trim(), actor, new byte[32], tokenSha, ct).ConfigureAwait(true);
        var markdown = BdPromptComposer.Ask(draft, new BdPromptHandle(runId, token));
        await _store.SetPromptShaAsync(runId, BdDoctrine.Sha(markdown), ct).ConfigureAwait(true);
        Status = $"Opened Claude on your question (run {runId}). Its answer appears under Doctrine when it reports back.";
        await LoadAsync(ct).ConfigureAwait(true);
        return new BdPreparedPrompt(runId, $"bd-ask-{runId}.md", markdown);
    }

    /// <summary>A synthesis run: the books into doctrine for the role, building on what is accepted and what is waiting.</summary>
    public async Task<BdPreparedPrompt> PrepareSynthesisAsync(IReadOnlyList<string> books, string? focus, string actor, CancellationToken ct)
    {
        // One synthesis at a time: two running together cannot see each other's cards, which is how duplicates got in.
        if (OpenSynthesis(await _store.ListRunsAsync(50, ct).ConfigureAwait(true), _clock()) is { } open)
            throw new ArgumentException($"A synthesis is still running (run {open.Id}, started {open.CreatedAtUtc.ToLocalTime():ddd HH:mm}). Wait for its cards before starting another.");
        var (token, tokenSha) = BdDoctrine.NewToken();
        var subject = string.IsNullOrWhiteSpace(focus) ? "the BD doctrine" : "the BD doctrine: " + focus.Trim();
        var runId = await _store.StartRunAsync("synthesis", Role, subject, focus, actor, new byte[32], tokenSha, ct).ConfigureAwait(true);
        var markdown = BdPromptComposer.Synthesis(new BdSynthesisInput(Role, actor, books,
            _cards.Where(c => c.Status == "accepted").ToList(), _cards.Where(c => c.Status == "proposed").ToList(), focus, _clock()),
            new BdPromptHandle(runId, token));
        await _store.SetPromptShaAsync(runId, BdDoctrine.Sha(markdown), ct).ConfigureAwait(true);
        Status = $"Opened Claude on the synthesis (run {runId}). Its cards appear here, waiting for you, when it reports back.";
        await LoadAsync(ct).ConfigureAwait(true);
        return new BdPreparedPrompt(runId, $"bd-synthesis-{runId}.md", markdown);
    }
}
