#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Briefs;

namespace Kor.Operations.App.Opportunities;

public sealed class PersonDossierViewModel : INotifyPropertyChanged, IAiContextProvider
{
    private readonly IBriefDataStore _store;
    private PersonBriefData? _brief;
    private string _statusMessage = "Ready.";

    public PersonDossierViewModel(IBriefDataStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProviderName => "BD Person Dossier";
    public bool HasData => Brief is not null;

    public PersonBriefData? Brief
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
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(CurrentRoleLine));
            OnPropertyChanged(nameof(HasCurrentEmployer));
            OnPropertyChanged(nameof(Email));
            OnPropertyChanged(nameof(HasEmail));
            OnPropertyChanged(nameof(Phone));
            OnPropertyChanged(nameof(HasPhone));
            OnPropertyChanged(nameof(LinkedinUrl));
            OnPropertyChanged(nameof(HasLinkedin));
            OnPropertyChanged(nameof(LastSeenDisplay));
            OnPropertyChanged(nameof(HasLastSeen));
            OnPropertyChanged(nameof(Notes));
            OnPropertyChanged(nameof(HasNotes));
            OnPropertyChanged(nameof(CurrentAffiliations));
            OnPropertyChanged(nameof(FormerAffiliations));
            OnPropertyChanged(nameof(RecentSignals));
            OnPropertyChanged(nameof(OpenActions));
            OnPropertyChanged(nameof(HasFormerAffiliations));
            OnPropertyChanged(nameof(HasSignals));
            OnPropertyChanged(nameof(HasActions));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string DisplayName => Brief?.DisplayName ?? "";

    public string CurrentRoleLine
    {
        get
        {
            var title = Brief?.CurrentTitle?.Trim();
            var employer = Brief?.CurrentEmployerName?.Trim();
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(employer))
            {
                return $"{title} at {employer}";
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                return title!;
            }

            return string.IsNullOrWhiteSpace(employer) ? "" : employer!;
        }
    }

    public bool HasCurrentEmployer => !string.IsNullOrWhiteSpace(Brief?.CurrentEmployerName);

    public string? Email => Brief?.Email;
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);

    public string? Phone => Brief?.Phone;
    public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);

    public string? LinkedinUrl => Brief?.LinkedinUrl;
    public bool HasLinkedin => !string.IsNullOrWhiteSpace(LinkedinUrl);

    public string LastSeenDisplay => Brief?.LastSeenAtUtc is { } seen
        ? seen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : "";

    public bool HasLastSeen => Brief?.LastSeenAtUtc is not null;

    public string? Notes => Brief?.Notes;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public IReadOnlyList<PersonAffiliationRow> CurrentAffiliations
        => Brief?.CurrentAffiliations ?? Array.Empty<PersonAffiliationRow>();

    public IReadOnlyList<PersonAffiliationRow> FormerAffiliations
        => Brief?.FormerAffiliations ?? Array.Empty<PersonAffiliationRow>();

    public IReadOnlyList<PersonSignalRow> RecentSignals
        => Brief?.RecentSignals ?? Array.Empty<PersonSignalRow>();

    public IReadOnlyList<PersonActionRow> OpenActions
        => Brief?.OpenActions ?? Array.Empty<PersonActionRow>();

    public bool HasFormerAffiliations => FormerAffiliations.Count > 0;
    public bool HasSignals => RecentSignals.Count > 0;
    public bool HasActions => OpenActions.Count > 0;

    public async Task LoadAsync(long intelPersonId, CancellationToken ct)
    {
        try
        {
            StatusMessage = "Loading person dossier...";
            Brief = await _store.GetPersonBriefAsync(intelPersonId, ct).ConfigureAwait(true);
            StatusMessage = Brief is null
                ? $"Intel person {intelPersonId} was not found."
                : $"Loaded person dossier for {Brief.DisplayName}.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Brief = null;
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public string BuildContext()
    {
        if (Brief is null)
        {
            return "BD Person Dossier - no person loaded.";
        }

        var role = string.IsNullOrWhiteSpace(CurrentRoleLine) ? "current role unknown" : CurrentRoleLine;
        return $"BD Person Dossier - person: {Brief.DisplayName}; role: {role}; current affiliations: {CurrentAffiliations.Count}; former affiliations: {FormerAffiliations.Count}; recent signals: {RecentSignals.Count}.";
    }

    public string BuildLocalContext() => BuildContext();

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
