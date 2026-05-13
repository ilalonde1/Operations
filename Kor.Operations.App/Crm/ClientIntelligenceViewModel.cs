#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Kor.Operations.Core;
using Kor.Operations.Services;

namespace Kor.Operations.App.Crm;

public sealed class ClientIntelligenceViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "Deltek Client Intelligence";

    private static readonly Brush ArNeutralBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3F, 0x53, 0x64)));
    private static readonly Brush ArWarningBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E)));

    private readonly IDeltekClientContextService _service;
    private string _statusMessage = "";
    private bool _isLoading;
    private DeltekClientIntelligence? _intelligence;
    private string? _clientId;

    public ClientIntelligenceViewModel(IDeltekClientContextService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

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

    public DeltekClientIntelligence? Intelligence
    {
        get => _intelligence;
        private set
        {
            if (SetField(ref _intelligence, value))
            {
                OnPropertyChanged(nameof(HasIntelligence));
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(HeaderText));
                OnPropertyChanged(nameof(BadgeRepeatVisible));
                OnPropertyChanged(nameof(BadgePriorWorkVisible));
                OnPropertyChanged(nameof(BadgeRecommendVisible));
                OnPropertyChanged(nameof(BadgeGovernmentVisible));
                OnPropertyChanged(nameof(BadgeCompetitorVisible));
                OnPropertyChanged(nameof(MemoText));
                OnPropertyChanged(nameof(LifetimeFeeDisplay));
                OnPropertyChanged(nameof(LatestProjectDisplay));
                OnPropertyChanged(nameof(ArSummaryDisplay));
                OnPropertyChanged(nameof(ArTotalOutstandingDisplay));
                OnPropertyChanged(nameof(ArOutstanding90PlusDisplay));
                OnPropertyChanged(nameof(ArOpenInvoiceCountDisplay));
                OnPropertyChanged(nameof(Ar90PlusBrush));
                OnPropertyChanged(nameof(DegradeWarningVisible));
                OnPropertyChanged(nameof(Projects));
                OnPropertyChanged(nameof(Contacts));
                OnPropertyChanged(nameof(RecentActivity));
            }
        }
    }

    public bool HasIntelligence => _intelligence is { ProjectCount: > 0 } || _intelligence?.Company is not null;

    public string WindowTitle => string.IsNullOrWhiteSpace(_intelligence?.ClientName)
        ? "Client Intelligence"
        : $"Client Intelligence - {_intelligence!.ClientName}";

    public string HeaderText => string.IsNullOrWhiteSpace(_intelligence?.ClientName)
        ? "Client Intelligence"
        : _intelligence!.ClientName;

    public bool BadgeRepeatVisible => _intelligence is { ProjectCount: > 0 };
    public bool BadgePriorWorkVisible => _intelligence?.Company?.PriorWork == true;
    public bool BadgeRecommendVisible => _intelligence?.Company?.Recommend == true;
    public bool BadgeGovernmentVisible => _intelligence?.Company?.GovernmentAgency == true;
    public bool BadgeCompetitorVisible => _intelligence?.Company?.Competitor == true;
    public bool DegradeWarningVisible => _intelligence?.HasDegradedSections == true;

    public string MemoText => _intelligence?.Company?.Memo ?? "";

    public string LifetimeFeeDisplay
    {
        get
        {
            if (_intelligence is null) return "";
            return $"{_intelligence.LifetimeFee.ToString("C0", CultureInfo.CurrentCulture)} across {_intelligence.ProjectCount} project(s)";
        }
    }

    public string LatestProjectDisplay
    {
        get
        {
            if (_intelligence is null || !_intelligence.LatestProjectStart.HasValue) return "";
            var name = string.IsNullOrWhiteSpace(_intelligence.LatestProjectName) ? "(unnamed)" : _intelligence.LatestProjectName;
            return $"{name} (opened {_intelligence.LatestProjectStart.Value:yyyy-MM-dd})";
        }
    }

    public string ArSummaryDisplay
    {
        get
        {
            if (_intelligence?.Ar is not { } ar) return "";
            return $"Outstanding {ar.TotalOutstanding.ToString("C0", CultureInfo.CurrentCulture)} - 90+ {ar.Outstanding90Plus.ToString("C0", CultureInfo.CurrentCulture)} - {ar.OpenInvoiceCount} invoice(s)";
        }
    }

    public string ArTotalOutstandingDisplay => _intelligence?.Ar is { } ar
        ? ar.TotalOutstanding.ToString("C0", CultureInfo.CurrentCulture)
        : "";

    public string ArOutstanding90PlusDisplay => _intelligence?.Ar is { } ar
        ? ar.Outstanding90Plus.ToString("C0", CultureInfo.CurrentCulture)
        : "";

    public string ArOpenInvoiceCountDisplay => _intelligence?.Ar is { } ar
        ? ar.OpenInvoiceCount.ToString("N0", CultureInfo.CurrentCulture)
        : "";

    public Brush Ar90PlusBrush => _intelligence?.Ar is { Outstanding90Plus: > 0m } ? ArWarningBrush : ArNeutralBrush;

    public IReadOnlyList<DeltekProjectSummary> Projects => _intelligence?.Projects ?? Array.Empty<DeltekProjectSummary>();
    public IReadOnlyList<DeltekContactSummary> Contacts => _intelligence?.Contacts ?? Array.Empty<DeltekContactSummary>();
    public IReadOnlyList<DeltekActivitySummary> RecentActivity => _intelligence?.RecentActivity ?? Array.Empty<DeltekActivitySummary>();

    public async Task LoadAsync(string deltekClientId, CancellationToken ct)
    {
        _clientId = deltekClientId;
        IsLoading = true;
        StatusMessage = $"Loading client {deltekClientId}";
        try
        {
            Intelligence = await _service.LoadAsync(deltekClientId, ct).ConfigureAwait(true);
            StatusMessage = Intelligence is null
                ? $"No Deltek client found for id '{deltekClientId}'."
                : Intelligence.HasDegradedSections
                    ? "Loaded - some sections were unavailable (see warning above)."
                    : "Loaded.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    public string ProviderName => Provider;
    public bool HasData => _intelligence is not null;

    public string BuildContext()
    {
        if (_intelligence is null) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine($"Deltek client {_intelligence.ClientId} - {_intelligence.ClientName}");
        sb.AppendLine($"  Lifetime fee: {LifetimeFeeDisplay}");
        if (_intelligence.LatestProjectStart.HasValue) sb.AppendLine($"  Latest project: {LatestProjectDisplay}");
        if (_intelligence.Ar is not null) sb.AppendLine($"  AR: {ArSummaryDisplay}");
        if (_intelligence.Company is { } c)
        {
            var flags = new List<string>();
            if (c.PriorWork) flags.Add("PriorWork");
            if (c.Recommend) flags.Add("Recommend");
            if (c.GovernmentAgency) flags.Add("GovernmentAgency");
            if (c.Competitor) flags.Add("Competitor");
            if (flags.Count > 0) sb.AppendLine($"  Flags: {string.Join(", ", flags)}");
            if (!string.IsNullOrWhiteSpace(c.Specialty)) sb.AppendLine($"  Specialty: {c.Specialty}");
            if (!string.IsNullOrWhiteSpace(c.Market)) sb.AppendLine($"  Market: {c.Market}");
        }

        if (_intelligence.Contacts.Count > 0)
        {
            sb.AppendLine($"  Contacts ({_intelligence.Contacts.Count}):");
            foreach (var p in _intelligence.Contacts.Take(5))
            {
                sb.AppendLine($"    {p.FirstName} {p.LastName} - {p.Title}; {p.Email}");
            }
        }

        // Methodology emission removed in Batch 92c — the MCP tool descriptions
        // + system prompt now carry KOR methodology, cached by the LLM after
        // first call. Each WPF BuildContext stays scope-only.
        return sb.ToString();
    }

    public string BuildLocalContext() => BuildContext();

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
