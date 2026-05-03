#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Kor.Operations.Core;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// ViewModel for <c>OpportunitiesWindow</c>. Holds the loaded list, the
/// selected row, and the heartbeat status string for the top-of-window banner.
/// Implements <see cref="IAiContextProvider"/> per the firm-wide rule that
/// every feature module exposes its data to the AI.
/// </summary>
public sealed class OpportunitiesViewModel : ObservableObject, IAiContextProvider
{
    public const string Provider = "Opportunities (BD)";

    // Heartbeat staleness thresholds. Mirror the FileSync convention: green
    // < 2x heartbeat interval, amber up to 5x, red beyond. The Worker beats
    // every 60s, so 2 minutes / 5 minutes here.
    private static readonly TimeSpan HeartbeatStaleAmber = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HeartbeatStaleRed = TimeSpan.FromMinutes(5);

    private readonly IOpportunityStore _store;
    private readonly IHeartbeatStore _heartbeatStore;

    // Frozen so the VM can hand them out cross-thread (XAML binds on UI thread but
    // RefreshHeartbeatAsync runs the assignment off the UI thread). Mirrors the
    // FileSync KPI-brush convention.
    private static readonly Brush HealthGreen = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22)));
    private static readonly Brush HealthAmber = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00)));
    private static readonly Brush HealthRed = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E)));
    private static readonly Brush HealthNeutral = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0x9B, 0xD1)));

    private OpportunityRowView? _selected;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private string _heartbeatLine = "Heartbeat: not yet loaded.";
    private string _heartbeatHealth = "Unknown";
    private Brush _heartbeatBrush = HealthNeutral;

    public OpportunitiesViewModel(IOpportunityStore store, IHeartbeatStore heartbeatStore)
    {
        _store = store;
        _heartbeatStore = heartbeatStore;
    }

    public ObservableCollection<OpportunityRowView> Opportunities { get; } = new();

    public OpportunityRowView? Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

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

    public string HeartbeatLine
    {
        get => _heartbeatLine;
        private set => SetField(ref _heartbeatLine, value);
    }

    /// <summary>One of <c>Green</c> / <c>Amber</c> / <c>Red</c> / <c>Unknown</c>.</summary>
    public string HeartbeatHealth
    {
        get => _heartbeatHealth;
        private set => SetField(ref _heartbeatHealth, value);
    }

    public Brush HeartbeatBrush
    {
        get => _heartbeatBrush;
        private set => SetField(ref _heartbeatBrush, value);
    }

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading…";
        try
        {
            var rows = await _store.ListAsync(ct).ConfigureAwait(true);

            var preservedKey = Selected?.OpportunityKey;
            Opportunities.Clear();
            foreach (var r in rows)
            {
                Opportunities.Add(new OpportunityRowView(r));
            }

            if (!string.IsNullOrEmpty(preservedKey))
            {
                Selected = Opportunities.FirstOrDefault(r => r.OpportunityKey == preservedKey);
            }

            await RefreshHeartbeatAsync(ct).ConfigureAwait(true);

            StatusMessage = rows.Count == 0
                ? "No opportunities yet — click \"New Opportunity\" to add one."
                : $"Loaded {rows.Count} opportunit{(rows.Count == 1 ? "y" : "ies")}.";
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

    public async Task<Opportunity> InsertAsync(Opportunity draft, string actor, CancellationToken ct)
    {
        var saved = await _store.InsertAsync(draft, actor, ct).ConfigureAwait(true);
        Opportunities.Insert(0, new OpportunityRowView(saved));
        Selected = Opportunities[0];
        StatusMessage = $"Inserted {saved.OpportunityKey}.";
        return saved;
    }

    public async Task<Opportunity> UpdateAsync(Opportunity edited, string actor, CancellationToken ct)
    {
        var saved = await _store.UpdateAsync(edited, actor, ct).ConfigureAwait(true);
        ReplaceRow(saved);
        StatusMessage = $"Updated {saved.OpportunityKey}.";
        return saved;
    }

    public async Task<Opportunity> ChangeStatusAsync(
        OpportunityRowView row,
        OpportunityStatus newStatus,
        string actor,
        CancellationToken ct)
    {
        var saved = await _store.ChangeStatusAsync(row.Id, newStatus, row.Model.RowVersion, actor, ct).ConfigureAwait(true);
        ReplaceRow(saved);
        StatusMessage = $"{saved.OpportunityKey}: {row.Status} → {newStatus}.";
        return saved;
    }

    private void ReplaceRow(Opportunity saved)
    {
        for (int i = 0; i < Opportunities.Count; i++)
        {
            if (Opportunities[i].Id == saved.Id)
            {
                Opportunities[i] = new OpportunityRowView(saved);
                Selected = Opportunities[i];
                return;
            }
        }

        // Should not happen for an UPDATE/ChangeStatus; treat as insert.
        Opportunities.Insert(0, new OpportunityRowView(saved));
        Selected = Opportunities[0];
    }

    private async Task RefreshHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            var rows = await _heartbeatStore.ListAsync(ct).ConfigureAwait(true);
            var hb = rows.FirstOrDefault(r => r.ServiceName == "Kor.Opportunities.Worker");
            if (hb is null)
            {
                HeartbeatLine = "Worker heartbeat: never seen.";
                HeartbeatHealth = "Red";
                HeartbeatBrush = HealthRed;
                return;
            }

            var age = DateTimeOffset.UtcNow - hb.LastBeatUtc.UtcDateTime;
            (HeartbeatHealth, HeartbeatBrush) = age switch
            {
                _ when age < HeartbeatStaleAmber => ("Green", HealthGreen),
                _ when age < HeartbeatStaleRed => ("Amber", HealthAmber),
                _ => ("Red", HealthRed),
            };

            HeartbeatLine = $"Worker {hb.MachineName} v{hb.Version ?? "?"} — last beat {Humanize(age)} ago.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            HeartbeatLine = $"Heartbeat read failed: {ex.GetType().Name}.";
            HeartbeatHealth = "Red";
            HeartbeatBrush = HealthRed;
        }
    }

    private static string Humanize(TimeSpan span) =>
        span.TotalSeconds < 90 ? $"{span.TotalSeconds:F0}s"
        : span.TotalMinutes < 90 ? $"{span.TotalMinutes:F0}m"
        : $"{span.TotalHours:F1}h";

    // ------------------------------------------------------------------------
    // IAiContextProvider
    // ------------------------------------------------------------------------

    public string ProviderName => Provider;

    public bool HasData => Opportunities.Count > 0;

    public string BuildContext()
    {
        // Firm-wide pursuit pipeline summary. Group by status, list deadlines
        // within 30 days, list anything in Pursuing/ProposalSubmitted (the hot list).
        var sb = new StringBuilder();
        sb.AppendLine($"Total opportunities tracked: {Opportunities.Count}.");

        var byStatus = Opportunities
            .GroupBy(r => r.Model.Status)
            .OrderBy(g => (int)g.Key)
            .ToList();
        if (byStatus.Count > 0)
        {
            sb.AppendLine("By status:");
            foreach (var g in byStatus)
            {
                sb.AppendLine($"  {g.Key}: {g.Count()}");
            }
        }

        var hot = Opportunities
            .Where(r => r.Model.Status is OpportunityStatus.Pursuing or OpportunityStatus.ProposalSubmitted)
            .OrderBy(r => r.Model.SubmissionDeadlineUtc ?? DateTimeOffset.MaxValue)
            .Take(20)
            .ToList();
        if (hot.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Hot list (Pursuing / ProposalSubmitted):");
            foreach (var r in hot)
            {
                var deadline = r.Model.SubmissionDeadlineUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
                sb.AppendLine($"  [{r.Model.Status}] {r.OpportunityKey} — {r.Name} ({r.BuyerName}); deadline {deadline}; owner {r.OwnerStaffId}");
            }
        }

        var imminent = Opportunities
            .Where(r => r.Model.SubmissionDeadlineUtc.HasValue
                        && r.Model.SubmissionDeadlineUtc.Value <= DateTimeOffset.UtcNow.AddDays(30)
                        && r.Model.Status is not OpportunityStatus.Won
                            and not OpportunityStatus.Lost
                            and not OpportunityStatus.NoBid
                            and not OpportunityStatus.Withdrawn)
            .OrderBy(r => r.Model.SubmissionDeadlineUtc!.Value)
            .Take(20)
            .ToList();
        if (imminent.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Deadlines within 30 days (open pursuits):");
            foreach (var r in imminent)
            {
                sb.AppendLine($"  {r.Model.SubmissionDeadlineUtc!.Value:yyyy-MM-dd} — {r.OpportunityKey} {r.Name} ({r.Model.Status})");
            }
        }

        return sb.ToString();
    }

    public string BuildLocalContext()
    {
        var s = Selected;
        if (s is null)
        {
            return string.Empty;
        }

        var m = s.Model;
        var sb = new StringBuilder();
        sb.AppendLine($"Selected opportunity: {m.OpportunityKey} — {m.Name}");
        sb.AppendLine($"Buyer: {m.BuyerName} ({m.BuyerType})");
        sb.AppendLine($"Status: {m.Status}");
        sb.AppendLine($"Discipline: {m.Discipline}");
        if (!string.IsNullOrWhiteSpace(s.Location))
        {
            sb.AppendLine($"Location: {s.Location}");
        }

        if (m.EstimatedValue.HasValue)
        {
            sb.AppendLine($"Estimated value: {m.EstimatedValue.Value:N0} {m.EstimatedValueCurrency}");
        }

        if (m.SubmissionDeadlineUtc.HasValue)
        {
            sb.AppendLine($"Deadline: {m.SubmissionDeadlineUtc.Value:yyyy-MM-dd HH:mm zzz}");
        }

        if (!string.IsNullOrWhiteSpace(m.OwnerStaffId))
        {
            sb.AppendLine($"Owner: {m.OwnerStaffId}");
        }

        if (m.RelevanceScore.HasValue)
        {
            sb.AppendLine($"Relevance: {m.RelevanceScore.Value} ({m.RelevanceTier})");
        }

        return sb.ToString();
    }
}
