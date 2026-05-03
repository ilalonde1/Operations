#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// ViewModel for <c>ScoringProfileWindow</c>. Hydrates the editable weight
/// rows from the cached <see cref="ScoringOptions"/>, persists user edits via
/// <see cref="IScoringOptionsAccessor"/>, and runs the all-rows recalc that
/// applies the new profile to every existing <see cref="Opportunity"/>.
/// </summary>
public sealed class ScoringProfileViewModel : ObservableObject
{
    private readonly IScoringOptionsAccessor _accessor;
    private readonly IOpportunityScoringService _scoringService;
    private readonly IOpportunityStore _store;

    private string _statusMessage = "Ready.";
    private bool _isBusy;
    private decimal _minScore;
    private decimal _maxScore;
    private decimal _hardRejectScore;
    private decimal _repeatDeveloperBonus;
    private decimal _deadlineFeasibleBonus;
    private decimal _deadlineCrunchPenalty;
    private int _deadlineWarningWindowDays;
    private decimal _minimumValueThresholdCad;
    private decimal _tierHighThreshold;
    private decimal _tierMediumThreshold;
    private decimal _tierLowThreshold;
    private string _hardRejectCountriesText = string.Empty;

    public ScoringProfileViewModel(
        IScoringOptionsAccessor accessor,
        IOpportunityScoringService scoringService,
        IOpportunityStore store)
    {
        _accessor = accessor;
        _scoringService = scoringService;
        _store = store;
    }

    public ObservableCollection<ScoringTermRow> PositiveTerms { get; } = new();

    public ObservableCollection<ScoringTermRow> NegativeTerms { get; } = new();

    public ObservableCollection<ScoringTermRow> RegionTerms { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public decimal MinScore { get => _minScore; set => SetField(ref _minScore, value); }
    public decimal MaxScore { get => _maxScore; set => SetField(ref _maxScore, value); }
    public decimal HardRejectScore { get => _hardRejectScore; set => SetField(ref _hardRejectScore, value); }
    public decimal RepeatDeveloperBonus { get => _repeatDeveloperBonus; set => SetField(ref _repeatDeveloperBonus, value); }
    public decimal DeadlineFeasibleBonus { get => _deadlineFeasibleBonus; set => SetField(ref _deadlineFeasibleBonus, value); }
    public decimal DeadlineCrunchPenalty { get => _deadlineCrunchPenalty; set => SetField(ref _deadlineCrunchPenalty, value); }
    public int DeadlineWarningWindowDays { get => _deadlineWarningWindowDays; set => SetField(ref _deadlineWarningWindowDays, value); }
    public decimal MinimumValueThresholdCad { get => _minimumValueThresholdCad; set => SetField(ref _minimumValueThresholdCad, value); }
    public decimal TierHighThreshold { get => _tierHighThreshold; set => SetField(ref _tierHighThreshold, value); }
    public decimal TierMediumThreshold { get => _tierMediumThreshold; set => SetField(ref _tierMediumThreshold, value); }
    public decimal TierLowThreshold { get => _tierLowThreshold; set => SetField(ref _tierLowThreshold, value); }

    /// <summary>One country term per line. Trimmed + de-duped on save.</summary>
    public string HardRejectCountriesText
    {
        get => _hardRejectCountriesText;
        set => SetField(ref _hardRejectCountriesText, value);
    }

    public void Load()
    {
        LoadFromOptions(_accessor.GetCurrent());
        StatusMessage = "Loaded current scoring profile.";
    }

    public void ResetToDefaults()
    {
        LoadFromOptions(ScoringOptions.KorDefaults());
        StatusMessage = "Reset to KOR defaults — click Save to persist.";
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var updated = BuildOptions();
            _ = await Task.Run(() => _accessor.Update(updated), ct).ConfigureAwait(true);
            // Reload from the accessor so any normalization (trimmed terms,
            // swapped Min/Max) shows up in the UI immediately.
            LoadFromOptions(_accessor.GetCurrent());
            StatusMessage = "Profile saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Re-scores every opportunity using the currently-cached profile and
    /// writes the updated row through <see cref="IOpportunityStore.UpdateAsync"/>.
    /// Concurrency conflicts (someone else just edited that row) are skipped,
    /// not retried — they'll get scored on their next save.
    /// </summary>
    public async Task<RecalcResult> RecalcAllAsync(string actorDisplay, CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = "Recalculating…";
        var result = new RecalcResult();
        try
        {
            var rows = await _store.ListAsync(ct).ConfigureAwait(true);
            var total = rows.Count;
            var processed = 0;

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                processed++;
                StatusMessage = $"Recalculating {processed}/{total}: {row.OpportunityKey}…";

                var score = _scoringService.Score(row);
                if (row.RelevanceScore == score.Score && row.RelevanceTier == score.Tier)
                {
                    result.Unchanged++;
                    continue;
                }

                var rescored = row with
                {
                    RelevanceScore = score.Score,
                    RelevanceTier = score.Tier,
                };

                try
                {
                    await _store.UpdateAsync(rescored, actorDisplay, ct).ConfigureAwait(true);
                    result.Updated++;
                }
                catch (OpportunityConcurrencyException)
                {
                    result.SkippedConcurrent++;
                }
            }

            StatusMessage = $"Recalc complete: {result.Updated} updated, {result.Unchanged} unchanged, {result.SkippedConcurrent} skipped.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Recalc cancelled.";
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Recalc failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        return result;
    }

    private void LoadFromOptions(ScoringOptions o)
    {
        PositiveTerms.Clear();
        foreach (var kv in o.PositiveTermWeights.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            PositiveTerms.Add(new ScoringTermRow(kv.Key, kv.Value));
        }

        NegativeTerms.Clear();
        foreach (var kv in o.NegativeTermWeights.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            NegativeTerms.Add(new ScoringTermRow(kv.Key, kv.Value));
        }

        RegionTerms.Clear();
        foreach (var kv in o.RegionTermWeights.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            RegionTerms.Add(new ScoringTermRow(kv.Key, kv.Value));
        }

        MinScore = o.MinScore;
        MaxScore = o.MaxScore;
        HardRejectScore = o.HardRejectScore;
        RepeatDeveloperBonus = o.RepeatDeveloperBonus;
        DeadlineFeasibleBonus = o.DeadlineFeasibleBonus;
        DeadlineCrunchPenalty = o.DeadlineCrunchPenalty;
        DeadlineWarningWindowDays = o.DeadlineWarningWindowDays;
        MinimumValueThresholdCad = o.MinimumValueThresholdCad;
        TierHighThreshold = o.TierHighThreshold;
        TierMediumThreshold = o.TierMediumThreshold;
        TierLowThreshold = o.TierLowThreshold;
        HardRejectCountriesText = string.Join(Environment.NewLine, o.HardRejectCountryTerms);
    }

    private ScoringOptions BuildOptions()
    {
        return new ScoringOptions
        {
            PositiveTermWeights = ToDictionary(PositiveTerms),
            NegativeTermWeights = ToDictionary(NegativeTerms),
            RegionTermWeights = ToDictionary(RegionTerms),
            HardRejectCountryTerms = (HardRejectCountriesText ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray(),
            MinScore = MinScore,
            MaxScore = MaxScore,
            HardRejectScore = HardRejectScore,
            RepeatDeveloperBonus = RepeatDeveloperBonus,
            DeadlineFeasibleBonus = DeadlineFeasibleBonus,
            DeadlineCrunchPenalty = DeadlineCrunchPenalty,
            DeadlineWarningWindowDays = DeadlineWarningWindowDays,
            MinimumValueThresholdCad = MinimumValueThresholdCad,
            TierHighThreshold = TierHighThreshold,
            TierMediumThreshold = TierMediumThreshold,
            TierLowThreshold = TierLowThreshold,
        };
    }

    private static Dictionary<string, decimal> ToDictionary(IEnumerable<ScoringTermRow> rows)
    {
        var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var term = (r.Term ?? string.Empty).Trim();
            if (term.Length > 0)
            {
                // Last write wins on duplicates; intentional - the editor lets
                // users replace a row by retyping the term.
                dict[term] = r.Weight;
            }
        }

        return dict;
    }

    public sealed class RecalcResult
    {
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int SkippedConcurrent { get; set; }
    }
}
