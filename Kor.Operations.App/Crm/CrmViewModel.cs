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

    private CrmEngagementRowView? _selected;
    private string _statusMessage = "Ready.";
    private bool _isLoading;

    public CrmViewModel(
        ICrmEngagementStore engagementStore,
        ICrmActivityStore activityStore,
        ICrmContactStore contactStore,
        IOpportunityStore opportunityStore)
    {
        _engagementStore = engagementStore;
        _activityStore = activityStore;
        _contactStore = contactStore;
        _opportunityStore = opportunityStore;
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

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
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

        return sb.ToString();
    }
}
