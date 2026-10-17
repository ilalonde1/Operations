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

public sealed record DoctrineCardView(DoctrineCardRow Card, DoctrineCardRecord Record)
{
    public long Id => Card.Id;
    public string Title => Card.Title;
    public string Play => Card.Play;
    public string Summary => Record.Summary;
    public string FlagText => Record.FlagText;
    public string Outcomes => $"{Record.Undecided} undecided · {Record.PursuitsWon} won · {Record.PursuitsLost} lost · {Record.PursuitsArchived} archived";
}

/// <summary>
/// The BD consigliere: asking Claude from the Director, and the doctrine it learns from. The NetworkOps Prompt Library method —
/// compose the prompt from live data when it is opened, open Claude Code on it, take the report back by one-time token, and let
/// only cards Ian accepts reach later prompts. Nothing here sends anything to anyone (CLAUDE.md rule 14: no API call).
/// </summary>
public sealed class BdDoctrineViewModel : ObservableObject
{
    private string _role = "BD";
    private int _loadVersion;
    public IReadOnlyList<string> Roles { get; } = new[] { "BD", "Operations" };
    public string Role { get => _role; private set => SetField(ref _role, value); }
    private readonly IBdDoctrineStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private IReadOnlyList<DoctrineCardRow> _cards = Array.Empty<DoctrineCardRow>();
    private IReadOnlyDictionary<long, DoctrineCardRecord> _records = new Dictionary<long, DoctrineCardRecord>();
    private string _status = "";
    private DoctrineCardView? _focusedCard;

    public BdDoctrineViewModel(IBdDoctrineStore store) : this(store, () => DateTimeOffset.Now) { }

    public BdDoctrineViewModel(IBdDoctrineStore store, Func<DateTimeOffset> clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ObservableCollection<DoctrineCardRow> Proposed { get; } = new();
    public ObservableCollection<DoctrineCardView> Accepted { get; } = new();
    public ObservableCollection<DoctrineCardView> NeedsLook { get; } = new();
    public IReadOnlyList<DoctrineCardRow> Cards => _cards;
    public IReadOnlyList<DoctrineCardRow> CitationCards { get; private set; } = Array.Empty<DoctrineCardRow>();
    public DoctrineCardView? FocusedCard { get => _focusedCard; private set => SetField(ref _focusedCard, value); }
    public bool HasNeedsLook => NeedsLook.Count > 0;
    public string RecordRule => DoctrineTrackRecord.Rule;
    public ObservableCollection<BdPromptRunView> Runs { get; } = new();
    public string Summary => $"{Accepted.Count} accepted · {Proposed.Count} waiting for you · {Runs.Count(r => r.Run.Outcome is null)} question(s) still with Claude";
    public string ProposedHeading => Proposed.Count == 0 ? "Nothing waiting for you" : $"Waiting for you: {Proposed.Count}";
    public bool HasProposed => Proposed.Count > 0;
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public void Say(string text) => Status = text;

    public async Task SelectRoleAsync(string role, CancellationToken ct)
    {
        if (!Roles.Contains(role)) throw new ArgumentException("Role must be BD or Operations.", nameof(role));
        if (Role == role) return;
        Role = role;
        _cards = Array.Empty<DoctrineCardRow>();
        _records = new Dictionary<long, DoctrineCardRecord>();
        FocusedCard = null;
        Proposed.Clear(); Accepted.Clear(); NeedsLook.Clear(); Runs.Clear();
        await LoadAsync(ct).ConfigureAwait(true);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        var role = Role;
        var version = ++_loadVersion;
        try
        {
            var cards = await _store.ListCardsAsync(role, ct).ConfigureAwait(true);
            var otherCards = await _store.ListCardsAsync(role == "BD" ? "Operations" : "BD", ct).ConfigureAwait(true);
            var records = (await _store.ListRecordsAsync(role, ct).ConfigureAwait(true)).ToDictionary(r => r.CardId);
            var runs = await _store.ListRunsAsync(role, 50, ct).ConfigureAwait(true);
            if (version != _loadVersion || role != Role) return;
            _cards = cards.Where(c => c.Role == role).ToArray();
            CitationCards = cards.Concat(otherCards).ToArray();
            _records = records;
            Proposed.Clear();
            foreach (var c in _cards.Where(c => c.Status == "proposed")) Proposed.Add(c);
            Accepted.Clear();
            foreach (var c in _cards.Where(c => c.Status == "accepted").OrderBy(c => c.Title, StringComparer.CurrentCulture))
                Accepted.Add(new DoctrineCardView(c, records.GetValueOrDefault(c.Id) ?? DoctrineCardRecord.Empty(c.Id)));
            NeedsLook.Clear();
            foreach (var c in Accepted.Where(c => c.Record.Flags.Count > 0)) NeedsLook.Add(c);
            if (FocusedCard is { } focused)
            {
                var card = _cards.FirstOrDefault(c => c.Id == focused.Id);
                FocusedCard = card is null ? null : new DoctrineCardView(card, records.GetValueOrDefault(card.Id) ?? DoctrineCardRecord.Empty(card.Id));
            }
            Runs.Clear();
            foreach (var r in runs.Where(r => r.Role == role).Take(50)) Runs.Add(new BdPromptRunView(r));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BD Director could not load the doctrine.");
            if (version != _loadVersion || role != Role) return;
            Status = "The doctrine could not be loaded. Refresh to try again.";
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ProposedHeading));
        OnPropertyChanged(nameof(HasProposed));
        OnPropertyChanged(nameof(HasNeedsLook));
        OnPropertyChanged(nameof(Cards));
        OnPropertyChanged(nameof(CitationCards));
    }

    public void FocusCard(long id)
    {
        var card = _cards.FirstOrDefault(c => c.Id == id && c.Status is "accepted" or "retired");
        FocusedCard = card is null ? null : Accepted.FirstOrDefault(c => c.Id == id)
            ?? new DoctrineCardView(card, _records.GetValueOrDefault(id) ?? DoctrineCardRecord.Empty(id));
    }

    public async Task<bool> RetireAsync(DoctrineCardView card, CancellationToken ct)
    {
        if (card.Card.Role != Role) { Status = "Select the card's role first."; return false; }
        var retired = await _store.RetireCardAsync(card.Id, "Ian", ct).ConfigureAwait(true);
        Status = retired ? $"Retired: {card.Title}" : "That card is no longer accepted. Refreshed.";
        await LoadAsync(ct).ConfigureAwait(true);
        return retired;
    }

    public static string AmendmentQuestion(DoctrineCardView card) =>
        $"Please review doctrine card #{card.Id}, “{card.Title}”, and propose an amendment using amends: {card.Id}. "
        + $"Its record: {card.Summary}; {card.Outcomes}. {card.FlagText}. Explain what the evidence supports.";

    public static IReadOnlyList<string> AmendmentFacts(DoctrineCardView card) => new[]
    {
        $"Card #{card.Id}: {card.Title}", card.Card.Plain, $"Principle: {card.Card.Principle}",
        $"Recognise: {card.Card.Recognise}", $"Play: {card.Card.Play}", $"Citations: {card.Card.Citations}",
        card.Summary, card.Outcomes, card.FlagText, DoctrineTrackRecord.Rule,
    };

    /// <summary>A synthesis started in the last two days that has not reported. Older ones are treated as abandoned.</summary>
    public static BdPromptRunRow? OpenSynthesis(IReadOnlyList<BdPromptRunRow> runs, DateTimeOffset now) =>
        runs.FirstOrDefault(r => r.Kind == "synthesis" && r.Outcome is null && r.CreatedAtUtc > now.AddDays(-2));

    /// <summary>Accepts every card still waiting, oldest first, so an amendment is decided after the card it amends.</summary>
    public async Task<int> AcceptAllAsync(string actor, CancellationToken ct)
    {
        var accepted = 0;
        var role = Role;
        foreach (var card in Proposed.Where(c => c.Role == role && c.Status == "proposed").OrderBy(c => c.Id).ToList())
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
        if (card.Role != Role) { Status = "Select the card's role first."; return false; }
        var done = await _store.DecideCardAsync(card.Id, accept, actor, ct).ConfigureAwait(true);
        Status = !done ? "That card was already decided. Refreshed."
            : accept ? $"Accepted: {card.Title}" + (card.AmendsCardId is { } a ? $" (card #{a} retired)" : "") : $"Rejected: {card.Title}";
        await LoadAsync(ct).ConfigureAwait(true);
        return done;
    }

    /// <summary>A question about today, a person or a pursuit, composed now from what the Director knows, with the doctrine that applies.</summary>
    public async Task<BdPreparedPrompt> PrepareAskAsync(string question, string subject, DoctrineContext context, IReadOnlyList<string> facts, string actor, CancellationToken ct, string role = "BD")
    {
        if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("Type the question first.", nameof(question));
        if (!Roles.Contains(role)) throw new ArgumentException("Role must be BD or Operations.", nameof(role));
        var cards = role == Role ? _cards : await _store.ListCardsAsync(role, ct).ConfigureAwait(true);
        var accepted = cards.Where(c => c.Role == role && c.Status == "accepted").ToList();
        var applies = accepted.Where(c => BdDoctrine.AppliesTo(c.AppliesTo, context)).ToList();
        var library = accepted.Except(applies).ToList();
        var (token, tokenSha) = BdDoctrine.NewToken();
        var draft = new BdAskInput(question.Trim(), actor, subject, context, facts, applies, library, _clock());
        // The prompt carries its run id, so the run is recorded first and the hash of the final text stored after.
        var runId = await _store.StartRunAsync("ask", role, subject, question.Trim(), actor, new byte[32], tokenSha, ct).ConfigureAwait(true);
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
        var role = Role;
        if (OpenSynthesis(await _store.ListRunsAsync(role, 50, ct).ConfigureAwait(true), _clock()) is { } open)
            throw new ArgumentException($"A synthesis is still running (run {open.Id}, started {open.CreatedAtUtc.ToLocalTime():ddd HH:mm}). Wait for its cards before starting another.");
        var now = _clock();
        var cards = await _store.ListCardsAsync(role, ct).ConfigureAwait(true);
        var records = await _store.ListRecordsAsync(role, ct).ConfigureAwait(true);
        var closed = role == "BD" ? await _store.ListClosedPursuitsAsync(now.AddDays(-90), now, ct).ConfigureAwait(true) : Array.Empty<ClosedPursuit>();
        var (token, tokenSha) = BdDoctrine.NewToken();
        var subject = "the " + role + " doctrine" + (string.IsNullOrWhiteSpace(focus) ? "" : ": " + focus.Trim());
        var runId = await _store.StartRunAsync("synthesis", role, subject, focus, actor, new byte[32], tokenSha, ct).ConfigureAwait(true);
        var markdown = BdPromptComposer.Synthesis(new BdSynthesisInput(role, actor, books,
            cards.Where(c => c.Status == "accepted").ToList(), cards.Where(c => c.Status == "proposed").ToList(), focus, now, records, closed),
            new BdPromptHandle(runId, token));
        await _store.SetPromptShaAsync(runId, BdDoctrine.Sha(markdown), ct).ConfigureAwait(true);
        Status = $"Opened Claude on the synthesis (run {runId}). Its cards appear here, waiting for you, when it reports back.";
        await LoadAsync(ct).ConfigureAwait(true);
        return new BdPreparedPrompt(runId, $"bd-synthesis-{runId}.md", markdown);
    }
}
