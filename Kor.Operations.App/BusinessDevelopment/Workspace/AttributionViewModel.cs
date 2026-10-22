#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// ViewModel behind <c>AttributionView</c> — the BD attribution scorecard.
/// Honest by construction: it shows the wins/fee/active counts the CRM
/// actually carries, plus the breakdowns that have data. Owner and win-rate
/// fill in as grabbed pursuits convert to wins carrying an owner and a loss
/// denominator (design §9), so the scorecard grows rather than fakes.
/// </summary>
public sealed class AttributionViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "BD Attribution (BD)";

    private readonly IBdAttributionStore _store;

    private BdAttributionSummary? _summary;
    private string _statusMessage = "Ready.";
    private bool _isLoading;

    public AttributionViewModel(IBdAttributionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public ObservableCollection<AttributionSourceRow> BySource { get; } = new();
    public ObservableCollection<AttributionOwnerRow> ByOwner { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public bool HasSummary => _summary is not null;

    public string WinsDisplay => _summary?.Wins.ToString(CultureInfo.CurrentCulture) ?? "—";
    public string WonFeeDisplay => _summary is null ? "—" : _summary.WonFee.ToString("C0", CultureInfo.CurrentCulture);
    public string ActiveDisplay => _summary?.ActivePursuits.ToString(CultureInfo.CurrentCulture) ?? "—";
    public string SubmittedDisplay => _summary?.Submitted.ToString(CultureInfo.CurrentCulture) ?? "—";

    /// <summary>
    /// Losses recorded. No win rate (Ian, 21 Oct 2026): most tracked pursuits were never closed won or lost, so a rate from
    /// these rows measures the record-keeping, not KOR.
    /// </summary>
    public string LostDisplay => _summary?.Lost.ToString(CultureInfo.CurrentCulture) ?? "—";

    /// <summary>One-line note keeping the scorecard honest about what is/ isn't tracked yet.</summary>
    public string MaturityNote
    {
        get
        {
            if (_summary is null) return string.Empty;
            var unattributed = _summary.ByOwner.FirstOrDefault(o => o.Owner == "(unattributed)")?.Wins ?? 0;
            // Only when some wins really have no person (audit M6: it read "The 0 wins recorded so far..." otherwise).
            if (unattributed > 0)
            {
                return $"Wins by person fill in as grabbed pursuits are won. The {unattributed} wins recorded so far "
                     + "came from the fee proposal tracker, which did not record who led them.";
            }
            return string.Empty;
        }
    }

    public bool HasMaturityNote => !string.IsNullOrEmpty(MaturityNote);

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading attribution…";
        try
        {
            var summary = await _store.GetSummaryAsync(ct).ConfigureAwait(true);
            _summary = summary;

            // Feed keys and logins in words (2026-10-21: "Deltek.CustomProposal", "M.FeeProposalTracking", "(unattributed)").
            // Sources that read the same in words are one row ("Grab.Forward" and "Grab.APC" are both "Grabbed in the app"; M6).
            BySource.Clear();
            foreach (var g in summary.BySource.GroupBy(s => PlainSource(s.Source)).OrderByDescending(g => g.Sum(s => s.Wins)))
            {
                BySource.Add(new AttributionSourceRow(g.Key, g.Sum(s => s.Wins), g.Sum(s => s.WonFee)));
            }

            ByOwner.Clear();
            foreach (var o in summary.ByOwner.Where(o => o.Owner != "(unattributed)"))
            {
                ByOwner.Add(o with { Owner = StaffDisplay.Short(o.Owner) });
            }

            foreach (var name in new[]
            {
                nameof(HasSummary), nameof(WinsDisplay), nameof(WonFeeDisplay), nameof(ActiveDisplay),
                nameof(SubmittedDisplay), nameof(LostDisplay), nameof(MaturityNote), nameof(HasMaturityNote),
            })
            {
                OnPropertyChanged(name);
            }

            StatusMessage = $"{Plural(summary.Wins, "win")} · {Plural(summary.ActivePursuits, "active pursuit")}.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string Plural(int n, string noun) => $"{n:N0} {noun}{(n == 1 ? "" : "s")}";

    internal static string PlainSource(string source) => source switch
    {
        "M.FeeProposalTracking" => "Fee proposal tracker",
        "Deltek.CustomProposal" => "Deltek, before the tracker (not in the wins above)",
        _ when source.StartsWith("Grab", StringComparison.OrdinalIgnoreCase) => "Grabbed in the app",
        _ => source,
    };

    // ===== IAiContextProvider =====

    public string ProviderName => Provider;

    public bool HasData => _summary is not null;

    public string BuildContext()
    {
        var s = _summary;
        if (s is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"BD attribution (CRM-sourced): {s.Wins} wins, won fee {s.WonFee.ToString("C0", CultureInfo.CurrentCulture)}, "
                    + $"{s.ActivePursuits} active pursuits, {s.Submitted} submitted, {s.Lost} lost.");
        if (s.BySource.Count > 0)
        {
            sb.AppendLine("Wins by source: " + string.Join(", ", s.BySource.Select(x => $"{x.Source} {x.Wins}")));
        }
        return sb.ToString();
    }

    public string BuildLocalContext() => string.Empty;
}
