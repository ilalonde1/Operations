#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Contracts;
using Kor.Operations.Core.Services;
using Serilog;

namespace Kor.Operations.App.Contracts;

/// <summary>
/// The Positions screen: what KOR has on file for a contract topic, by client. Reads the clause library
/// (KorTransmittals dbo.vw_ContractPositions) through <see cref="IContractClauseStore"/>; never writes.
/// </summary>
public sealed class ContractPositionsViewModel : ObservableObject
{
    public static readonly TopicOption AnyTopic = new("", "Any topic");
    public const string AnyClient = "Any client";

    private readonly IContractClauseStore _store;
    private TopicOption _selectedTopic;
    private string _selectedClient = AnyClient;
    private string _searchText = string.Empty;
    private bool _signedOnly;
    private bool _isBusy;
    private string _status = "Choose a topic, then Search.";
    private ContractPositionRow? _selectedRow;

    public ContractPositionsViewModel(IContractClauseStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Topics = [AnyTopic, .. ContractTopics.All.Select(t => new TopicOption(t.Key, t.Label))];
        _selectedTopic = Topics[1]; // limitation of liability: the question Rory asks first
        Clients = [AnyClient];
    }

    public IReadOnlyList<TopicOption> Topics { get; }
    public ObservableCollection<string> Clients { get; }
    public ObservableCollection<ContractPositionRow> Rows { get; } = new();

    public TopicOption SelectedTopic { get => _selectedTopic; set => SetField(ref _selectedTopic, value ?? AnyTopic); }
    public string SelectedClient { get => _selectedClient; set => SetField(ref _selectedClient, string.IsNullOrWhiteSpace(value) ? AnyClient : value); }
    public string SearchText { get => _searchText; set => SetField(ref _searchText, value ?? string.Empty); }
    public bool SignedOnly { get => _signedOnly; set => SetField(ref _signedOnly, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetField(ref _isBusy, value)) OnPropertyChanged(nameof(CanSearch)); } }
    public bool CanSearch => !IsBusy;
    public string Status { get => _status; private set => SetField(ref _status, value); }

    public ContractPositionRow? SelectedRow
    {
        get => _selectedRow;
        set { if (SetField(ref _selectedRow, value)) { OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(HasNoSelection)); } }
    }
    public bool HasSelection => SelectedRow is not null;
    public bool HasNoSelection => SelectedRow is null;

    /// <summary>The query the current filters describe. Public so a test can see it without a store.</summary>
    public ContractPositionQuery BuildQuery() => new(
        Topic: string.IsNullOrEmpty(SelectedTopic.Key) ? null : SelectedTopic.Key,
        ClientName: SelectedClient == AnyClient ? null : SelectedClient,
        Text: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
        SignedOnly: SignedOnly);

    public async Task LoadClientsAsync(CancellationToken ct = default)
    {
        try
        {
            var clients = await _store.ListClientsAsync(ct).ConfigureAwait(true);
            // Merge rather than clear: clearing the items makes the combo drop its selection mid-binding and show
            // blank (seen live 2026-10-28). "Any client" stays at index 0 and stays selected.
            var wanted = new List<string> { AnyClient };
            wanted.AddRange(clients.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase));
            for (var i = Clients.Count - 1; i >= 1; i--)
            {
                if (!wanted.Contains(Clients[i], StringComparer.OrdinalIgnoreCase)) Clients.RemoveAt(i);
            }
            for (var i = 0; i < wanted.Count; i++)
            {
                if (i >= Clients.Count) Clients.Add(wanted[i]);
                else if (!string.Equals(Clients[i], wanted[i], StringComparison.OrdinalIgnoreCase)) Clients.Insert(i, wanted[i]);
            }
            if (!Clients.Contains(SelectedClient)) SelectedClient = AnyClient;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Loading the contract clients list failed.");
            Status = $"Could not load the client list: {ex.Message}";
        }
    }

    public async Task SearchAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var query = BuildQuery();
            var positions = await _store.SearchAsync(query, ct).ConfigureAwait(true);
            var (shown, folded) = FoldCopies(positions);
            Rows.Clear();
            foreach (var p in shown) Rows.Add(new ContractPositionRow(p));
            SelectedRow = Rows.FirstOrDefault();
            Status = Describe(shown, query) + (folded > 0 ? $" {folded} further {(folded == 1 ? "copy" : "copies")} of the same clauses folded." : string.Empty);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Searching the contract clause library failed.");
            Status = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The same signed agreement is often on file three times (the folder copy, the email it came in, a scan), and
    /// each copy carries the same clause, sometimes under a different section number when the scan's numbering was
    /// read differently. One row per (job, opening words) is what a reader wants. The fullest copy wins (a scan
    /// can cut a clause short), in the order the store returned, signed first. Returns the rows and the fold count.
    /// </summary>
    public static (List<ContractPosition> Shown, int Folded) FoldCopies(IReadOnlyList<ContractPosition> positions)
    {
        var best = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // key → index in order
        var order = new List<ContractPosition>(positions.Count);
        foreach (var p in positions)
        {
            var opening = string.Join(" ", p.ClauseText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(12));
            var key = $"{p.SourceKey}|{opening}";
            if (best.TryGetValue(key, out var at))
            {
                if (p.Words > order[at].Words) order[at] = p;
            }
            else
            {
                best[key] = order.Count;
                order.Add(p);
            }
        }
        return (order, positions.Count - order.Count);
    }

    /// <summary>"N clauses from M documents across K clients, L signed" — the counts a reader checks first.</summary>
    public static string Describe(IReadOnlyCollection<ContractPosition> positions, ContractPositionQuery query)
    {
        if (positions.Count == 0)
            return query.Topic is null && query.ClientName is null && query.Text is null
                ? "Nothing on file."
                : "Nothing on file for that filter.";
        var docs = positions.Select(p => (p.FilePath, p.Part)).Distinct().Count();
        var clients = positions.Select(p => p.ClientName).Where(c => !string.IsNullOrEmpty(c)).Distinct().Count();
        var signed = positions.Count(p => p.IsSigned);
        var capped = positions.Count >= query.Take ? $" (first {query.Take})" : string.Empty;
        return $"{N(positions.Count, "clause")}{capped} from {N(docs, "document")} across {N(clients, "client")}, {signed} in signed agreements.";
    }

    private static string N(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    /// <summary>
    /// The library stores paths relative to E:\ on KOR-FS01 ("Projects/Projects/03 Residential/…"). The shares are
    /// Projects = E:\Projects and Management = E:\Management, so a reader opens the file from the share, not E$.
    /// </summary>
    public static string ToUncPath(string libraryPath)
    {
        var p = (libraryPath ?? string.Empty).Replace('/', '\\').TrimStart('\\');
        if (p.StartsWith(@"Projects\", StringComparison.OrdinalIgnoreCase)) return @"\\Kor-fs01\" + p;
        if (p.StartsWith(@"Management\", StringComparison.OrdinalIgnoreCase)) return @"\\Kor-fs01\" + p;
        return @"\\Kor-fs01\E$\" + p;
    }
}

public sealed record TopicOption(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A grid row: the position plus the short forms the grid shows.</summary>
public sealed class ContractPositionRow
{
    public ContractPositionRow(ContractPosition position)
    {
        Position = position ?? throw new ArgumentNullException(nameof(position));
        // A pursuit without a job carries its Project Opportunities folder as the key ("OPP:Fraser Health Authority").
        Client = !string.IsNullOrWhiteSpace(position.ClientName) ? position.ClientName
               : position.SourceKey.StartsWith("OPP:", StringComparison.OrdinalIgnoreCase) ? position.SourceKey[4..].Trim() + " (pursuit)"
               : "(no Deltek client)";
        Job = position.Job ?? string.Empty;
        Year = position.FileDate?.Year.ToString() ?? string.Empty;
        Status = position.IsSigned ? "Signed" : "Latest on file";
        Section = position.Section;
        Heading = Clip(position.Heading ?? string.Empty, 60);
        Preview = Clip(Flatten(position.ClauseText), 160);
        Words = position.Words;
        Text = Readable(position.ClauseText);
        TopicLabels = string.Join(", ", position.Topics.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(ContractTopics.LabelOf));
        SourcePath = ContractPositionsViewModel.ToUncPath(position.FilePath);
        SourceLabel = string.IsNullOrEmpty(position.Part)
            ? System.IO.Path.GetFileName(position.FilePath)
            : $"{System.IO.Path.GetFileName(position.FilePath)} › {position.Part}";
    }

    public ContractPosition Position { get; }
    public string Client { get; }
    public string Job { get; }
    public string Year { get; }
    public string Status { get; }
    public string Section { get; }
    public string Heading { get; }
    public string Preview { get; }
    public int Words { get; }
    public string Text { get; }
    public string TopicLabels { get; }
    public string SourcePath { get; }
    public string SourceLabel { get; }

    private static string Flatten(string s) => string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The clause as extracted keeps the PDF's line breaks mid-sentence; join those, keep blank-line paragraph
    /// breaks, and drop the e-signature footers a scan carries ("DocuSign Envelope ID: …"). The stored text is untouched.</summary>
    public static string Readable(string s)
    {
        var paragraphs = s.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var kept = paragraphs
            .Select(p => string.Join(" ", p.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                           .Select(l => l.Trim())
                                           .Where(l => l.Length > 0 && !l.StartsWith("DocuSign Envelope ID", StringComparison.OrdinalIgnoreCase))))
            .Where(p => p.Length > 0);
        return string.Join("\n\n", kept);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
