#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.MajorProjects;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class PursuitBriefViewModel : INotifyPropertyChanged
{
    private readonly IPursuitBriefStore _store;
    private PursuitBrief? _brief;
    private string _statusMessage = "Ready.";

    public PursuitBriefViewModel(IPursuitBriefStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
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

    public string KorEdgeDisplay => string.IsNullOrWhiteSpace(Brief?.KorEdge) ? "coming with Deltek fusion." : Brief!.KorEdge!;

    public bool KorEdgeIsPlaceholder => string.IsNullOrWhiteSpace(Brief?.KorEdge);

    public string ThePlayDisplay => string.IsNullOrWhiteSpace(Brief?.ThePlay) ? "coming with the AI Crucible." : Brief!.ThePlay!;

    public bool ThePlayIsPlaceholder => string.IsNullOrWhiteSpace(Brief?.ThePlay);

    public string FitScoreDisplay => Brief?.FitScore is { } score ? score.ToString("0.###") : "coming with the AI Crucible.";

    public bool FitScoreIsPlaceholder => Brief?.FitScore.HasValue != true;

    public async Task LoadAsync(long mpiId, CancellationToken ct)
    {
        try
        {
            StatusMessage = "Loading pursuit brief...";
            Brief = await _store.GetBriefForProjectAsync(mpiId, ct).ConfigureAwait(true);
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

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
