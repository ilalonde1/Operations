#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.Crm;

/// <summary>
/// ViewModel behind <c>CrmWindow</c>. Holds the engagement list (left
/// master), the activity log + contacts for the selected engagement
/// (right detail), and exposes <see cref="IAiContextProvider"/> so the
/// pipeline plus pursuit notes feed the AI context.
/// </summary>
public sealed class CrmViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "CRM (BD)";

    private readonly ICrmEngagementStore _engagementStore;
    private readonly ICrmActivityStore _activityStore;
    private readonly ICrmContactStore _contactStore;
    private readonly IOpportunityStore _opportunityStore;
    private readonly IDeltekClientContextService _deltekContextService;

    private CrmEngagementRowView? _selected;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private DeltekClientIntelligence? _deltekContext;
    private CrmAnalyticsSnapshot? _analytics;

    public CrmViewModel(
        ICrmEngagementStore engagementStore,
        ICrmActivityStore activityStore,
        ICrmContactStore contactStore,
        IOpportunityStore opportunityStore,
        IDeltekClientContextService deltekContextService)
    {
        _engagementStore = engagementStore;
        _activityStore = activityStore;
        _contactStore = contactStore;
        _opportunityStore = opportunityStore;
        _deltekContextService = deltekContextService;
    }

    public ObservableCollection<CrmEngagementRowView> Engagements { get; } = new();

    public ObservableCollection<CrmActivityRowView> Activities { get; } = new();

    public ObservableCollection<CrmContactRowView> Contacts { get; } = new();

    public IReadOnlyList<CrmEngagementStage> StageOptions { get; } = Enum.GetValues<CrmEngagementStage>();

    public IReadOnlyList<CrmActivityType> ActivityTypeOptions { get; } = Enum.GetValues<CrmActivityType>();

    public CrmEngagementRowView? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                _ = LoadDetailAsync(CancellationToken.None);
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public void SetStatusMessage(string message)
    {
        StatusMessage = message ?? string.Empty;
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    /// <summary>
    /// Deltek roll-up for the selected engagement's client. Null when the
    /// engagement's Opportunity has no DeltekClientId or the lookup hasn't
    /// landed yet. Refreshed every time <see cref="Selected"/> changes.
    /// </summary>
    public DeltekClientIntelligence? DeltekContext
    {
        get => _deltekContext;
        private set
        {
            if (SetField(ref _deltekContext, value))
            {
                OnPropertyChanged(nameof(DeltekContextSummary));
                OnPropertyChanged(nameof(HasDeltekContext));
            }
        }
    }

    public bool HasDeltekContext => _deltekContext is { ProjectCount: > 0 };

    /// <summary>
    /// Aggregate roll-up of the loaded engagements: stage breakdown, win rate
    /// total + by buyer type / owner, average won/lost fees, average pursuit
    /// duration. Refreshed every <see cref="LoadAsync"/>.
    /// </summary>
    public CrmAnalyticsSnapshot? Analytics
    {
        get => _analytics;
        private set
        {
            if (SetField(ref _analytics, value))
            {
                OnPropertyChanged(nameof(AnalyticsHeadline));
                OnPropertyChanged(nameof(AnalyticsBuyerTypeSummary));
                OnPropertyChanged(nameof(HasAnalytics));
            }
        }
    }

    public bool HasAnalytics => _analytics is { TotalEngagements: > 0 };

    /// <summary>One-line headline: total / win rate / avg won fee.</summary>
    public string AnalyticsHeadline
    {
        get
        {
            if (_analytics is null || _analytics.TotalEngagements == 0) return string.Empty;
            var won = _analytics.Won;
            var lost = _analytics.Lost;
            var rate = (won + lost) > 0 ? _analytics.WinRate.ToString("P0") : "—";
            var avgWonFee = _analytics.AvgWonProposedFee > 0
                ? $"  •  avg won fee {_analytics.AvgWonProposedFee.ToString("C0", CultureInfo.CurrentCulture)}"
                : string.Empty;
            return $"{_analytics.TotalEngagements} engagement(s)  •  {won}W / {lost}L  •  win rate {rate}{avgWonFee}";
        }
    }

    /// <summary>Top 3 buyer types by win rate (with at least 1 resolved engagement).</summary>
    public string AnalyticsBuyerTypeSummary
    {
        get
        {
            if (_analytics is null) return string.Empty;
            var rows = _analytics.ByBuyerType
                .Where(b => b.Won + b.Lost > 0)
                .OrderByDescending(b => b.WinRate)
                .ThenByDescending(b => b.Won + b.Lost)
                .Take(3)
                .Select(b => $"{b.Bucket} {b.Won}/{b.Won + b.Lost} ({b.WinRate:P0})");
            return string.Join("  •  ", rows);
        }
    }

    /// <summary>One-line UI summary of the Deltek roll-up. Empty when no context.</summary>
    public string DeltekContextSummary
    {
        get
        {
            if (_deltekContext is null) return string.Empty;
            var fee = _deltekContext.LifetimeFee.ToString("C0", CultureInfo.CurrentCulture);
            var last = _deltekContext.LatestProjectStart.HasValue
                ? _deltekContext.LatestProjectStart.Value.ToString("yyyy-MM-dd")
                : "—";
            return $"{_deltekContext.ClientName}: {_deltekContext.ProjectCount} project(s), lifetime fee {fee}, last opened {last}.";
        }
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading engagements…";
        try
        {
            var engagements = await _engagementStore.ListAsync(ct).ConfigureAwait(true);
            var opportunities = await _opportunityStore.ListAsync(ct).ConfigureAwait(true);
            var oppById = opportunities.ToDictionary(o => o.Id);

            var preservedId = Selected?.Id;
            Engagements.Clear();
            foreach (var e in engagements)
            {
                oppById.TryGetValue(e.OpportunityId, out var opp);
                Engagements.Add(new CrmEngagementRowView(e, opp));
            }

            Selected = preservedId.HasValue
                ? Engagements.FirstOrDefault(r => r.Id == preservedId.Value) ?? Engagements.FirstOrDefault()
                : Engagements.FirstOrDefault();

            // Refresh the analytics snapshot off the same data we just loaded.
            // Pure projection — no extra DB round-trip.
            Analytics = CrmAnalyticsService.Compute(engagements, oppById);

            StatusMessage = engagements.Count == 0
                ? "No CRM engagements yet — promote an opportunity from the Opportunities window to start tracking."
                : $"Loaded {engagements.Count} engagement{(engagements.Count == 1 ? "" : "s")}.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadDetailAsync(CancellationToken ct)
    {
        Activities.Clear();
        Contacts.Clear();
        DeltekContext = null;
        if (Selected is null)
        {
            return;
        }

        try
        {
            var engagementId = Selected.Id;
            var activities = await _activityStore.ListByEngagementAsync(engagementId, ct).ConfigureAwait(true);
            var contacts = await _contactStore.ListByEngagementAsync(engagementId, ct).ConfigureAwait(true);

            foreach (var a in activities)
            {
                Activities.Add(new CrmActivityRowView(a));
            }

            foreach (var c in contacts)
            {
                Contacts.Add(new CrmContactRowView(c));
            }

            // Phase 5c: pull the Deltek client roll-up if the linked Opportunity
            // has a Deltek client mapping. ODBC failure leaves DeltekContext null
            // — never blocks the rest of the detail load.
            var deltekId = Selected.Opportunity?.DeltekClientId;
            if (!string.IsNullOrWhiteSpace(deltekId))
            {
                try
                {
                    DeltekContext = await _deltekContextService.LoadAsync(deltekId, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Deltek client lookup failed: {ex.GetType().Name}: {ex.Message}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Detail load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task<CrmEngagement> ChangeStageAsync(CrmEngagementRowView row, CrmEngagementStage newStage, string actor, CancellationToken ct)
    {
        var updated = row.Engagement with
        {
            Stage = newStage,
            ClosedAtUtc = IsTerminal(newStage) ? (row.Engagement.ClosedAtUtc ?? DateTimeOffset.UtcNow) : row.Engagement.ClosedAtUtc,
        };

        var saved = await _engagementStore.UpdateAsync(updated, actor, ct).ConfigureAwait(true);
        ReplaceEngagement(saved);
        StatusMessage = $"{row.OpportunityKey}: stage → {newStage}.";
        return saved;
    }

    public async Task<CrmEngagement> SaveEngagementAsync(CrmEngagement edited, string actor, CancellationToken ct)
    {
        var saved = edited.Id == 0
            ? await _engagementStore.InsertAsync(edited, actor, ct).ConfigureAwait(true)
            : await _engagementStore.UpdateAsync(edited, actor, ct).ConfigureAwait(true);

        ReplaceEngagement(saved);
        StatusMessage = $"Saved engagement {saved.Id}.";
        return saved;
    }

    public async Task<CrmActivity> AppendActivityAsync(long engagementId, CrmActivityType type, string subject, string? body, string actor, CancellationToken ct)
    {
        var activity = new CrmActivity
        {
            EngagementId = engagementId,
            ActivityType = type,
            Subject = subject,
            Body = body,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        };

        var saved = await _activityStore.AppendAsync(activity, actor, ct).ConfigureAwait(true);
        Activities.Insert(0, new CrmActivityRowView(saved));
        StatusMessage = $"Logged {type}: {subject}.";
        return saved;
    }

    public async Task<CrmContact> AddContactAsync(long engagementId, string displayName, string? role, string? email, string? phone, bool isPrimary, string actor, CancellationToken ct)
    {
        var contact = new CrmContact
        {
            EngagementId = engagementId,
            DisplayName = displayName,
            Role = role,
            Email = email,
            Phone = phone,
            IsPrimary = isPrimary,
        };

        var saved = await _contactStore.InsertAsync(contact, actor, ct).ConfigureAwait(true);
        Contacts.Add(new CrmContactRowView(saved));
        StatusMessage = $"Added contact {displayName}.";
        return saved;
    }

    public async Task DeleteContactAsync(CrmContactRowView row, CancellationToken ct)
    {
        await _contactStore.DeleteAsync(row.Id, ct).ConfigureAwait(true);
        Contacts.Remove(row);
        StatusMessage = $"Removed contact {row.DisplayName}.";
    }

    private void ReplaceEngagement(CrmEngagement saved)
    {
        for (int i = 0; i < Engagements.Count; i++)
        {
            if (Engagements[i].Id == saved.Id)
            {
                var opp = Engagements[i].Opportunity;
                Engagements[i] = new CrmEngagementRowView(saved, opp);
                Selected = Engagements[i];
                return;
            }
        }
    }

    private static bool IsTerminal(CrmEngagementStage s)
        => s is CrmEngagementStage.Won or CrmEngagementStage.Lost or CrmEngagementStage.Withdrawn;

    // ------------------------------------------------------------------------
    // IAiContextProvider
    // ------------------------------------------------------------------------

    public string ProviderName => Provider;

    public bool HasData => Engagements.Count > 0;

    public string BuildContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Total CRM engagements: {Engagements.Count}.");

        var byStage = Engagements
            .GroupBy(r => r.Engagement.Stage)
            .OrderBy(g => (int)g.Key)
            .ToList();
        if (byStage.Count > 0)
        {
            sb.AppendLine("By stage:");
            foreach (var g in byStage)
            {
                sb.AppendLine($"  {g.Key}: {g.Count()}");
            }
        }

        var hot = Engagements
            .Where(r => r.Engagement.Stage is
                CrmEngagementStage.ProposalSubmitted
                or CrmEngagementStage.Presenting
                or CrmEngagementStage.Negotiating)
            .OrderByDescending(r => r.Engagement.UpdatedAtUtc)
            .Take(15)
            .ToList();
        if (hot.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Active pursuits in Submitted / Presenting / Negotiating:");
            foreach (var r in hot)
            {
                sb.AppendLine($"  [{r.Engagement.Stage}] {r.OpportunityKey} {r.ProjectName} ({r.Buyer}) — owner {r.OwnerDisplay}");
            }
        }

        var won = Engagements.Count(r => r.Engagement.Stage == CrmEngagementStage.Won);
        var lost = Engagements.Count(r => r.Engagement.Stage == CrmEngagementStage.Lost);
        if (won + lost > 0)
        {
            var rate = (double)won / (won + lost);
            sb.AppendLine();
            sb.AppendLine($"Trailing win rate (won vs lost): {won} / {won + lost} = {rate:P0}");
        }

        if (_analytics is { TotalEngagements: > 0 } a && (a.Won + a.Lost) > 0)
        {
            var topBuyer = a.ByBuyerType
                .Where(b => b.Won + b.Lost > 0)
                .OrderByDescending(b => b.WinRate)
                .ThenByDescending(b => b.Won + b.Lost)
                .FirstOrDefault();
            if (topBuyer is not null)
            {
                sb.AppendLine($"Best-performing buyer type: {topBuyer.Bucket} ({topBuyer.Won}/{topBuyer.Won + topBuyer.Lost} = {topBuyer.WinRate:P0}).");
            }

            if (a.AvgPursuitDuration.HasValue)
            {
                sb.AppendLine($"Avg time from open to outcome: {a.AvgPursuitDuration.Value.TotalDays:F0} day(s).");
            }
        }

        // BD concept methodology (Batch 72). The 9-stage funnel, WinRate
        // denominator, and pursuit-duration window all have specific
        // KOR meaning (Withdrawn / OnHold are excluded from rate, etc.).
        // Surface the dictionary so AI cites those exact semantics
        // instead of inventing generic CRM definitions.
        var methodology = Kor.Operations.Financials.FinancialMetricDefinitions.BuildAiMethodologyBlock(new[]
        {
            "Bd_EngagementStage", "Bd_WinRate", "Bd_PursuitDuration",
            "Bd_BuyerType",
        });
        if (methodology != null)
        {
            sb.AppendLine();
            sb.AppendLine("BD concept methodology (so you can explain the funnel / win rate / buyer-type breakdown the way KOR defines them):");
            sb.Append(methodology);
        }

        return sb.ToString();
    }

    public string BuildLocalContext()
    {
        if (Selected is null)
        {
            return string.Empty;
        }

        var e = Selected.Engagement;
        var sb = new StringBuilder();
        sb.AppendLine($"Selected engagement: {Selected.OpportunityKey} — {Selected.ProjectName}");
        sb.AppendLine($"Buyer: {Selected.Buyer}");
        sb.AppendLine($"Stage: {e.Stage}");
        if (!string.IsNullOrWhiteSpace(e.OwnerStaffId))
        {
            sb.AppendLine($"Owner: {e.OwnerStaffId}");
        }

        if (e.ProposedFee.HasValue)
        {
            sb.AppendLine($"Proposed fee: {e.ProposedFee.Value.ToString("C0", CultureInfo.CurrentCulture)}");
        }

        if (e.TargetMargin.HasValue)
        {
            sb.AppendLine($"Target margin: {e.TargetMargin.Value:0.#}%");
        }

        if (Activities.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recent activity:");
            foreach (var a in Activities.Take(8))
            {
                sb.AppendLine($"  {a.OccurredDisplay} {a.TypeDisplay}: {a.Subject}");
            }
        }

        if (Contacts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Contacts:");
            foreach (var c in Contacts)
            {
                var primary = c.IsPrimary ? " (primary)" : string.Empty;
                sb.AppendLine($"  {c.DisplayName}{primary} — {c.Role}; {c.Email}; {c.Phone}");
            }
        }

        if (_deltekContext is { } dc && (dc.ProjectCount > 0 || dc.Company is not null))
        {
            sb.AppendLine();
            DeltekClientIntelligenceFormatter.Append(sb, dc);
        }

        return sb.ToString();
    }
}
