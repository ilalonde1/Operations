#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Data.MajorProjects;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class PursuitBriefViewModel : INotifyPropertyChanged
{
    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");
    private readonly IPursuitBriefStore _store;
    private readonly IDeltekClientContextService _deltek;
    private PursuitBrief? _brief;
    private string? _korEdgeDisplay;
    private string _statusMessage = "Ready.";

    public PursuitBriefViewModel(IPursuitBriefStore store, IDeltekClientContextService deltek)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _deltek = deltek ?? throw new ArgumentNullException(nameof(deltek));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PursuitBrief? Brief
    {
        get => _brief;
        private set
        {
            if (Equals(_brief, value))
            {
                return;
            }

            _brief = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProjectName));
            OnPropertyChanged(nameof(HasProcurementProfile));
            OnPropertyChanged(nameof(NoProcurementProfile));
            OnPropertyChanged(nameof(KorEdgeDisplay));
            OnPropertyChanged(nameof(KorEdgeIsPlaceholder));
            OnPropertyChanged(nameof(ThePlayDisplay));
            OnPropertyChanged(nameof(ThePlayIsPlaceholder));
            OnPropertyChanged(nameof(FitScoreDisplay));
            OnPropertyChanged(nameof(FitScoreIsPlaceholder));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string ProjectName => Brief?.Project.ProjectName ?? "";

    public bool HasProcurementProfile
        => !string.IsNullOrWhiteSpace(Brief?.OwnerProcurement.ProcurementMethod)
            || !string.IsNullOrWhiteSpace(Brief?.OwnerProcurement.RosterProgram)
            || !string.IsNullOrWhiteSpace(Brief?.OwnerProcurement.EvaluationCriteria)
            || !string.IsNullOrWhiteSpace(Brief?.OwnerProcurement.BudgetCadence);

    public bool NoProcurementProfile => !HasProcurementProfile;

    public string KorEdgeDisplay => string.IsNullOrWhiteSpace(_korEdgeDisplay)
        ? "No prior KOR work on record with this owner."
        : _korEdgeDisplay!;

    public bool KorEdgeIsPlaceholder => string.IsNullOrWhiteSpace(_korEdgeDisplay);

    public string ThePlayDisplay => string.IsNullOrWhiteSpace(Brief?.ThePlay) ? "coming with the AI Crucible." : Brief!.ThePlay!;

    public bool ThePlayIsPlaceholder => string.IsNullOrWhiteSpace(Brief?.ThePlay);

    public string FitScoreDisplay => Brief?.FitScore is { } score ? score.ToString("0.###") : "coming with the AI Crucible.";

    public bool FitScoreIsPlaceholder => Brief?.FitScore.HasValue != true;

    public async Task LoadAsync(long mpiId, CancellationToken ct)
    {
        try
        {
            StatusMessage = "Loading pursuit brief...";
            KorEdgeRaw = null;
            Brief = await _store.GetBriefForProjectAsync(mpiId, ct).ConfigureAwait(true);
            await LoadOwnerEdgeAsync(Brief, ct).ConfigureAwait(true);
            StatusMessage = Brief is null
                ? $"Major Projects Inventory row {mpiId} was not found."
                : $"Loaded pursuit brief for {Brief.Project.ProjectName}.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private string? KorEdgeRaw
    {
        get => _korEdgeDisplay;
        set
        {
            if (SetField(ref _korEdgeDisplay, value, nameof(KorEdgeDisplay)))
            {
                OnPropertyChanged(nameof(KorEdgeIsPlaceholder));
            }
        }
    }

    private async Task LoadOwnerEdgeAsync(PursuitBrief? brief, CancellationToken ct)
    {
        var ownerClendorClientId = brief?.OwnerClendorClientId?.Trim();
        if (string.IsNullOrWhiteSpace(ownerClendorClientId))
        {
            return;
        }

        try
        {
            var intelligence = await _deltek.LoadAsync(ownerClendorClientId, ct).ConfigureAwait(true);
            KorEdgeRaw = BuildKorEdgeDisplay(intelligence);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            KorEdgeRaw = null;
        }
    }

    private static string? BuildKorEdgeDisplay(DeltekClientIntelligence? intelligence)
    {
        if (intelligence is null)
        {
            return null;
        }

        var flags = new List<string>();
        if (intelligence.Company?.PriorWork == true)
        {
            flags.Add("Prior work");
        }

        if (intelligence.Company?.Recommend == true)
        {
            flags.Add("Recommended");
        }

        if (intelligence.ProjectCount <= 0
            && intelligence.LifetimeFee <= 0m
            && !intelligence.LatestProjectStart.HasValue
            && flags.Count == 0)
        {
            return null;
        }

        var parts = new List<string>
        {
            $"KOR client: {intelligence.ProjectCount:N0} projects",
            $"{intelligence.LifetimeFee.ToString("C0", CanadianCulture)} lifetime",
        };

        if (intelligence.LatestProjectStart.HasValue)
        {
            parts.Add($"last active {intelligence.LatestProjectStart.Value:yyyy}");
        }

        parts.AddRange(flags);
        return string.Join(" - ", parts);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
