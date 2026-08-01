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

    /// <summary>Win rate, or an honest "no losses yet" when there is no denominator.</summary>
    public string WinRateDisplay
    {
        get
        {
            if (_summary is null) return "—";
            var decided = _summary.Wins + _summary.Lost;
            if (decided == 0) return "—";
            if (_summary.Lost == 0) return "100% (no losses recorded yet)";
            return ((double)_summary.Wins / decided).ToString("P0", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>One-line note keeping the scorecard honest about what is/ isn't tracked yet.</summary>
    public string MaturityNote
    {
        get
        {
            if (_summary is null) return string.Empty;
            var unattributed = _summary.ByOwner.FirstOrDefault(o => o.Owner == "(unattributed)")?.Wins ?? 0;
            if (unattributed > 0 || _summary.Lost == 0)
            {
                return "Owner and win-rate dimensions fill in as grabbed pursuits convert to wins — "
                     + $"{unattributed} historical win(s) predate owner tracking.";
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

            BySource.Clear();
            foreach (var s in summary.BySource)
            {
                BySource.Add(s);
            }

            ByOwner.Clear();
            foreach (var o in summary.ByOwner)
            {
                ByOwner.Add(o);
            }

            foreach (var name in new[]
            {
                nameof(HasSummary), nameof(WinsDisplay), nameof(WonFeeDisplay), nameof(ActiveDisplay),
                nameof(SubmittedDisplay), nameof(WinRateDisplay), nameof(MaturityNote), nameof(HasMaturityNote),
            })
            {
                OnPropertyChanged(name);
            }

            StatusMessage = $"{summary.Wins} win(s) · {summary.ActivePursuits} active pursuit(s).";
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
