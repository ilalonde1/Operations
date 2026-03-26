#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Kor.Operations.App;
using Kor.Operations.Data;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.PMTools;

public sealed class WorkloadMeetingPanelViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan NotesSaveDelay = TimeSpan.FromMilliseconds(800);

    private readonly IWorkloadMeetingStore _store;
    private readonly string _currentUserUpn;
    private readonly ILogger<WorkloadMeetingPanelViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly object _notesGate = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private WorkloadMeeting? _selectedMeeting;
    private string _meetingNotes = string.Empty;
    private bool _isBusy;
    private bool _suppressMeetingNotesSave;
    private int _busyCount;
    private int _notesSaveVersion;
    private Guid? _pendingNotesMeetingId;
    private string? _pendingNotesValue;
    private string _activityText = string.Empty;
    private string? _meetingError;

    public WorkloadMeetingPanelViewModel(
        IWorkloadMeetingStore store,
        string currentUserUpn,
        ILogger<WorkloadMeetingPanelViewModel> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _currentUserUpn = currentUserUpn ?? string.Empty;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        Meetings = new ObservableCollection<WorkloadMeeting>();
        CurrentProjects = new ObservableCollection<WorkloadMeetingProject>();
        NewMeetingCommand = new AsyncRelayCommand(_ => NewMeetingAsync());
        SetPriorityCommand = new AsyncRelayCommand(ExecuteSetPriorityAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WorkloadMeeting> Meetings { get; }

    public WorkloadMeeting? SelectedMeeting
    {
        get => _selectedMeeting;
        set
        {
            if (ReferenceEquals(_selectedMeeting, value))
            {
                return;
            }

            var previousMeeting = _selectedMeeting;
            _selectedMeeting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCurrentMeeting));
            OnPropertyChanged(nameof(IsViewingPastMeeting));
            _ = HandleSelectedMeetingChangedAsync(previousMeeting, value);
        }
    }

    public bool IsCurrentMeeting => SelectedMeeting != null && Meetings.Count > 0 && SelectedMeeting.Id == Meetings[0].Id;

    public bool IsViewingPastMeeting => SelectedMeeting != null && Meetings.Count > 0 && !IsCurrentMeeting;

    public string MeetingNotes
    {
        get => _meetingNotes;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_meetingNotes, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _meetingNotes = normalized;
            if (SelectedMeeting != null)
            {
                SelectedMeeting.Notes = normalized;
            }

            OnPropertyChanged();

            if (_suppressMeetingNotesSave || SelectedMeeting == null)
            {
                return;
            }

            ScheduleMeetingNotesSave(SelectedMeeting.Id, normalized);
        }
    }

    public ObservableCollection<WorkloadMeetingProject> CurrentProjects { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Short status label shown in the UI. Empty when idle.</summary>
    public string ActivityText
    {
        get => _activityText;
        private set
        {
            var v = value ?? string.Empty;
            if (_activityText == v) return;
            _activityText = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsActive));
        }
    }

    public bool IsActive => !string.IsNullOrEmpty(_activityText);

    /// <summary>Non-null when the last operation failed. Null when healthy.</summary>
    public string? MeetingError
    {
        get => _meetingError;
        private set
        {
            if (_meetingError == value) return;
            _meetingError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMeetingError));
        }
    }

    public bool HasMeetingError => _meetingError != null;

    public ICommand NewMeetingCommand { get; }

    public ICommand SetPriorityCommand { get; }

    public async Task UpsertPriorityFromUiAsync(string wbs1, int priority)
    {
        var selection = SelectedMeeting;
        if (selection == null || string.IsNullOrWhiteSpace(wbs1)) return;
        await RunBusyAsync(async () =>
        {
            ActivityText = "Saving\u2026";
            try
            {
                await _store.UpsertProjectPriorityAsync(selection.Id, wbs1, priority, notes: null).ConfigureAwait(false);
                var projects = await _store.GetProjectsForMeetingAsync(selection.Id).ConfigureAwait(false);
                await _dispatcher.InvokeAsync(() =>
                {
                    CurrentProjects.Clear();
                    foreach (var project in projects) CurrentProjects.Add(project);
                });
            }
            catch (Exception ex)
            {
                MeetingError = "Failed to save priority.";
                _logger.LogError(ex, "Failed to upsert priority for {Wbs1} in meeting {MeetingId}.", wbs1, selection.Id);
            }
        }).ConfigureAwait(false);
        ActivityText = string.Empty;
    }

    public async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            ActivityText = "Loading\u2026";
            MeetingError = null;
            try
            {
                await _store.EnsureTablesAsync().ConfigureAwait(false);
                var meetings = await _store.GetAllMeetingsAsync().ConfigureAwait(false);

                await _dispatcher.InvokeAsync(() =>
                {
                    Meetings.Clear();
                    foreach (var meeting in meetings)
                    {
                        Meetings.Add(meeting);
                    }
                });

                if (meetings.Count == 0)
                {
                    await ApplyMeetingSelectionAsync(null, Array.Empty<WorkloadMeetingProject>()).ConfigureAwait(false);
                    return;
                }

                var selected = meetings[0];
                var projects = await _store.GetProjectsForMeetingAsync(selected.Id).ConfigureAwait(false);
                await ApplyMeetingSelectionAsync(selected, projects).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MeetingError = "Failed to load meetings. Check your connection.";
                _logger.LogError(ex, "Failed to load workload meetings.");
            }
        }).ConfigureAwait(false);
        ActivityText = string.Empty;
    }

    private async Task HandleSelectedMeetingChangedAsync(WorkloadMeeting? previousMeeting, WorkloadMeeting? currentMeeting)
    {
        try
        {
            if (previousMeeting != null)
            {
                await FlushPendingNotesSaveAsync(previousMeeting.Id).ConfigureAwait(false);
            }

            if (currentMeeting == null)
            {
                await ApplyMeetingSelectionAsync(null, Array.Empty<WorkloadMeetingProject>()).ConfigureAwait(false);
                return;
            }

            await RunBusyAsync(async () =>
            {
                ActivityText = "Loading\u2026";
                try
                {
                    var projects = await _store.GetProjectsForMeetingAsync(currentMeeting.Id).ConfigureAwait(false);
                    await ApplyMeetingSelectionAsync(currentMeeting, projects).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    MeetingError = "Failed to load meeting data.";
                    _logger.LogError(ex, "Failed to load workload meeting projects for meeting {MeetingId}.", currentMeeting.Id);
                }
            }).ConfigureAwait(false);
            ActivityText = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle workload meeting selection change.");
        }
    }

    private async Task NewMeetingAsync()
    {
        await RunBusyAsync(async () =>
        {
            ActivityText = "Creating meeting\u2026";
            MeetingError = null;
            try
            {
                await FlushPendingNotesSaveAsync(SelectedMeeting?.Id).ConfigureAwait(false);

                var previousMeeting = await _dispatcher.InvokeAsync(() => Meetings.Count > 0 ? Meetings[0] : null);
                var newMeeting = await _store.CreateMeetingAsync(DateTime.Today, _currentUserUpn).ConfigureAwait(false);

                if (previousMeeting != null)
                {
                    await _store.CarryForwardProjectsAsync(previousMeeting.Id, newMeeting.Id).ConfigureAwait(false);
                }

                var projects = await _store.GetProjectsForMeetingAsync(newMeeting.Id).ConfigureAwait(false);

                await _dispatcher.InvokeAsync(() =>
                {
                    Meetings.Insert(0, newMeeting);
                });

                await ApplyMeetingSelectionAsync(newMeeting, projects).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MeetingError = "Failed to create a new meeting.";
                _logger.LogError(ex, "Failed to create a new workload meeting.");
            }
        }).ConfigureAwait(false);
        ActivityText = string.Empty;
    }

    private async Task ExecuteSetPriorityAsync(object? parameter)
    {
        var selection = SelectedMeeting;
        if (selection == null)
        {
            return;
        }

        if (!TryGetPriorityParameter(parameter, out var wbs1, out var priority))
        {
            _logger.LogWarning("Invalid SetPriorityCommand parameter for workload meeting.");
            return;
        }

        try
        {
            await UpsertPriorityFromUiAsync(wbs1, priority).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update workload priority for meeting {MeetingId} and WBS1 {Wbs1}.",
                selection.Id,
                wbs1);
        }
    }

    private void ScheduleMeetingNotesSave(Guid meetingId, string notes)
    {
        var version = Interlocked.Increment(ref _notesSaveVersion);
        lock (_notesGate)
        {
            _pendingNotesMeetingId = meetingId;
            _pendingNotesValue = notes;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(NotesSaveDelay, _disposeCts.Token).ConfigureAwait(false);
                if (version != Volatile.Read(ref _notesSaveVersion))
                {
                    return;
                }

                Guid? pendingMeetingId;
                string? pendingNotes;
                lock (_notesGate)
                {
                    pendingMeetingId = _pendingNotesMeetingId;
                    pendingNotes = _pendingNotesValue;
                }

                if (pendingMeetingId == meetingId)
                {
                    await ExecuteMeetingNotesSaveAsync(meetingId, pendingNotes).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to debounce workload meeting notes save for meeting {MeetingId}.", meetingId);
            }
        });
    }

    private async Task FlushPendingNotesSaveAsync(Guid? meetingId)
    {
        if (meetingId == null)
        {
            return;
        }

        Guid? pendingMeetingId;
        string? pendingNotes;
        lock (_notesGate)
        {
            pendingMeetingId = _pendingNotesMeetingId;
            pendingNotes = _pendingNotesValue;
        }

        if (pendingMeetingId != meetingId.Value)
        {
            return;
        }

        Interlocked.Increment(ref _notesSaveVersion);
        await ExecuteMeetingNotesSaveAsync(meetingId.Value, pendingNotes).ConfigureAwait(false);
    }

    private async Task ExecuteMeetingNotesSaveAsync(Guid meetingId, string? notes)
    {
        await RunBusyAsync(async () =>
        {
            ActivityText = "Saving\u2026";
            try
            {
                await _store.SaveMeetingNotesAsync(meetingId, notes).ConfigureAwait(false);

                lock (_notesGate)
                {
                    if (_pendingNotesMeetingId == meetingId)
                    {
                        _pendingNotesMeetingId = null;
                        _pendingNotesValue = null;
                    }
                }
            }
            catch (Exception ex)
            {
                MeetingError = "Failed to save notes.";
                _logger.LogError(ex, "Failed to save workload meeting notes for meeting {MeetingId}.", meetingId);
            }
        }).ConfigureAwait(false);
        ActivityText = string.Empty;
    }

    private async Task ApplyMeetingSelectionAsync(WorkloadMeeting? meeting, System.Collections.Generic.IEnumerable<WorkloadMeetingProject> projects)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            _suppressMeetingNotesSave = true;
            try
            {
                if (!ReferenceEquals(_selectedMeeting, meeting))
                {
                    _selectedMeeting = meeting;
                    OnPropertyChanged(nameof(SelectedMeeting));
                }

                CurrentProjects.Clear();
                foreach (var project in projects)
                {
                    CurrentProjects.Add(project);
                }

                MeetingNotes = meeting?.Notes ?? string.Empty;
                OnPropertyChanged(nameof(IsCurrentMeeting));
                OnPropertyChanged(nameof(IsViewingPastMeeting));
            }
            finally
            {
                _suppressMeetingNotesSave = false;
            }
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        await SetBusyStateAsync(increment: true).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            await SetBusyStateAsync(increment: false).ConfigureAwait(false);
        }
    }

    private async Task SetBusyStateAsync(bool increment)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            _busyCount += increment ? 1 : -1;
            if (_busyCount < 0)
            {
                _busyCount = 0;
            }

            IsBusy = _busyCount > 0;
        });
    }

    private bool TryGetPriorityParameter(object? parameter, out string wbs1, out int priority)
    {
        switch (parameter)
        {
            case ValueTuple<string, int> tuple:
                wbs1 = tuple.Item1;
                priority = tuple.Item2;
                return !string.IsNullOrWhiteSpace(wbs1);

            case Tuple<string, int> tuple:
                wbs1 = tuple.Item1;
                priority = tuple.Item2;
                return !string.IsNullOrWhiteSpace(wbs1);

            default:
                wbs1 = string.Empty;
                priority = 0;
                return false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}
