#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
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
    private long _meetingSelectionGeneration;
    private string _meetingNotes = string.Empty;
    private bool _isBusy;
    private bool _suppressMeetingNotesSave;
    private int _busyCount;
    private int _notesSaveVersion;
    private Guid? _pendingNotesMeetingId;
    private string? _pendingNotesValue;
    private string _activityText = string.Empty;
    private string? _meetingError;
    // Round 52i: default ON — Jim opens straight into the simple meeting view
    // (priority, notes, % billed, risk); "Show $ Detail" reveals the full grid.
    private bool _isMeetingMode = true;
    private bool _suppressProjectNotesRelay;
    private readonly ConcurrentDictionary<string, int> _projectNotesVersions = new(StringComparer.OrdinalIgnoreCase);

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
        SaveMeetingCommand = new AsyncRelayCommand(_ => SaveMeetingAsync());
        ToggleMeetingModeCommand = new AsyncRelayCommand(_ => { IsMeetingMode = !IsMeetingMode; return Task.CompletedTask; });
        PriorityProjects = new ObservableCollection<WorkloadMeetingProjectRow>();
        PriorityProjects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPriorityProjects));
        // Round 52: chips summarize the selected meeting's priorities (P1 ×3 …).
        // CurrentProjects is only ever mutated on the dispatcher, so one hook
        // covers every rebuild path (load, selection change, upsert refresh).
        CurrentProjects.CollectionChanged += (_, _) => RebuildPriorityChips();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Round 52f (review finding 4): raised on the UI thread whenever
    /// a project's meeting notes change through ANY surface (board, grid
    /// editor, orphan panel) so the window can mirror the text onto the
    /// matching grid row — without it, board edits never reached
    /// PmProjectRow.MeetingNotes and a later grid edit overwrote the newer
    /// board text. Args: (wbs1, notes).</summary>
    public event Action<string, string>? ProjectNotesUpdated;

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
            var gen = System.Threading.Interlocked.Increment(ref _meetingSelectionGeneration);
            _ = HandleSelectedMeetingChangedAsync(previousMeeting, value, gen);
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

    public ObservableCollection<WorkloadMeetingProjectRow> PriorityProjects { get; }

    public bool HasPriorityProjects => PriorityProjects.Count > 0;

    /// <summary>Round 52: per-priority counts for the toolbar chips (P1 ×3 …),
    /// rebuilt whenever CurrentProjects changes. Clicking a chip jumps the
    /// window's PM grids to the first row at that priority.</summary>
    public ObservableCollection<WorkloadMeetingPriorityChip> PriorityChips { get; } = new();

    private void RebuildPriorityChips()
    {
        PriorityChips.Clear();
        foreach (var chip in CurrentProjects
                     .Where(p => p.Priority >= 1 && p.Priority <= 5)
                     .GroupBy(p => p.Priority)
                     .OrderBy(g => g.Key)
                     .Select(g => new WorkloadMeetingPriorityChip { Priority = g.Key, Count = g.Count() }))
        {
            PriorityChips.Add(chip);
        }
    }

    public void SetPriorityProjectRows(System.Collections.Generic.IEnumerable<WorkloadMeetingProjectRow> rows)
    {
        foreach (var existing in PriorityProjects)
            existing.NotesChanged -= OnProjectNotesChanged;

        PriorityProjects.Clear();

        foreach (var row in ApplySortOrder(rows))
        {
            row.NotesChanged += OnProjectNotesChanged;
            PriorityProjects.Add(row);
        }
    }

    /// <summary>
    /// Round 39b (T2.001): project <see cref="CurrentProjects"/> into the
    /// <see cref="PriorityProjects"/> grid. Owned by the VM so the meeting
    /// window shows priorities even when it is opened in isolation (without
    /// PmCapacityWindow). The optional <paramref name="enrich"/> lookup lets
    /// the capacity window inject ProjectName / PmName from its own
    /// PmToolsViewModel rows.
    ///
    /// Round 40 (R4-T2.001): when no enricher is supplied, preserve any
    /// existing enriched ProjectName / PmName already on PriorityProjects
    /// instead of falling back to the Wbs1 number. This matters after a
    /// successful priority save: <see cref="UpsertPriorityFromUiAsync"/>
    /// rebuilds CurrentProjects, the capacity window's CollectionChanged
    /// handler fires during the rebuild and enriches via its lookup, and
    /// THEN the VM calls this method with no enricher. Without the preserve
    /// step that final call clobbered the enriched names back to Wbs1.
    /// </summary>
    public void RefreshPriorityProjects(Func<string, (string? Name, string? Pm)>? enrich = null)
    {
        // Snapshot existing enrichment (Wbs1 -> ProjectName/PmName) when we're
        // running without an enricher, so we keep what the capacity window
        // already wrote.
        var preserved = enrich is null
            ? PriorityProjects.ToDictionary(p => p.Wbs1, p => (p.ProjectName, p.PmName), StringComparer.OrdinalIgnoreCase)
            : null;

        var projected = CurrentProjects.Select(p =>
        {
            string? name = null;
            string? pm = null;
            if (enrich is not null)
            {
                var hit = enrich(p.Wbs1);
                name = hit.Name;
                pm = hit.Pm;
            }
            else if (preserved is not null && preserved.TryGetValue(p.Wbs1, out var prior))
            {
                // Treat "ProjectName == Wbs1" as un-enriched so we don't lock
                // in the fallback name forever.
                if (!string.IsNullOrWhiteSpace(prior.ProjectName)
                    && !string.Equals(prior.ProjectName, p.Wbs1, StringComparison.OrdinalIgnoreCase))
                {
                    name = prior.ProjectName;
                }
                if (!string.IsNullOrWhiteSpace(prior.PmName))
                {
                    pm = prior.PmName;
                }
            }

            return new WorkloadMeetingProjectRow
            {
                MeetingId = p.MeetingId,
                Wbs1 = p.Wbs1,
                Priority = p.Priority,
                Notes = p.Notes ?? string.Empty,
                ProjectName = !string.IsNullOrWhiteSpace(name) ? name! : p.Wbs1,
                PmName = pm ?? string.Empty,
            };
        });
        SetPriorityProjectRows(projected);
    }

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

    public ICommand SaveMeetingCommand { get; }

    /// <summary>
    /// Round 52: Meeting Mode reshapes the PM Groups grid for running the
    /// meeting in the room \u2014 deep-dive financial columns hide, PM groups
    /// auto-expand. The window owns the visual reactions; this is just the
    /// switch (lives here so the grid's column bindings, the toolbar button,
    /// and the window code-behind all observe one source of truth).
    /// </summary>
    public bool IsMeetingMode
    {
        get => _isMeetingMode;
        private set
        {
            if (_isMeetingMode == value) return;
            _isMeetingMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MeetingModeToggleLabel));
        }
    }

    // In Meeting Mode (the simple default) the button offers the escape to
    // financials; out of it, the button offers the way back to the simple view.
    public string MeetingModeToggleLabel => _isMeetingMode ? "Show $ Detail" : "\u2190 Meeting View";

    public ICommand ToggleMeetingModeCommand { get; }

    /// <summary>
    /// Round 39b (T2.002): now returns whether the priority was persisted.
    /// The capacity window uses this to revert <c>MeetingPriority</c> on the
    /// affected row when the store rejects the change \u2014 without it the row
    /// kept showing the new priority while the database had the old one.
    /// </summary>
    public async Task<bool> UpsertPriorityFromUiAsync(string wbs1, int priority)
    {
        var selection = SelectedMeeting;
        if (selection == null || string.IsNullOrWhiteSpace(wbs1)) return false;
        // Snapshot the current meeting selection generation so we can detect if the user switched meetings
        // while our async work was in flight.
        var genAtStart = System.Threading.Interlocked.Read(ref _meetingSelectionGeneration);
        var ok = false;
        await RunBusyAsync(async () =>
        {
            ActivityText = "Saving\u2026";
            MeetingError = null;
            try
            {
                await _store.UpsertProjectPriorityAsync(selection.Id, wbs1, priority, notes: null).ConfigureAwait(false);
                var projects = await _store.GetProjectsForMeetingAsync(selection.Id).ConfigureAwait(false);
                // Drop stale refresh if the user switched meetings after our save began.
                if (System.Threading.Interlocked.Read(ref _meetingSelectionGeneration) != genAtStart)
                {
                    ok = true; // the save itself succeeded; only the local refresh was dropped
                    return;
                }
                await _dispatcher.InvokeAsync(() =>
                {
                    // Round 52f (review finding 1): a notes edit younger than
                    // the 600ms debounce exists only in memory — this DB
                    // re-read would clobber it in the UI, and the close-flush
                    // would then persist the stale text over the debounced
                    // save (permanent loss). Every edit path updates
                    // CurrentProjects.Notes synchronously, so in-memory is
                    // never older than the DB for this client; overlay it.
                    var inMemoryNotes = CurrentProjects.ToDictionary(p => p.Wbs1, p => p.Notes, StringComparer.OrdinalIgnoreCase);
                    CurrentProjects.Clear();
                    foreach (var project in projects)
                    {
                        if (inMemoryNotes.TryGetValue(project.Wbs1, out var liveNotes))
                            project.Notes = liveNotes;
                        CurrentProjects.Add(project);
                    }
                    // Round 39b (T2.001): own the projection so the meeting
                    // window shows priorities without help from the capacity
                    // window. Basic enrichment only; the capacity window will
                    // overwrite with ProjectName/PmName when it's open.
                    RefreshPriorityProjects();
                });
                ok = true;
            }
            catch (Exception ex)
            {
                MeetingError = "Failed to save priority.";
                _logger.LogError(ex, "Failed to upsert priority for {Wbs1} in meeting {MeetingId}.", wbs1, selection.Id);
            }
        }).ConfigureAwait(false);
        ActivityText = string.Empty;
        return ok;
    }

    public async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            ActivityText = "Loading\u2026";
            MeetingError = null;
            try
            {
                // Fresh user-initiated action — invalidate any in-flight stale async loads.
                var gen = System.Threading.Interlocked.Increment(ref _meetingSelectionGeneration);

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
                    await ApplyMeetingSelectionAsync(null, Array.Empty<WorkloadMeetingProject>(), gen).ConfigureAwait(false);
                    return;
                }

                var selected = meetings[0];
                var projects = await _store.GetProjectsForMeetingAsync(selected.Id).ConfigureAwait(false);
                await ApplyMeetingSelectionAsync(selected, projects, gen).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MeetingError = "Failed to load meetings. Check your connection.";
                _logger.LogError(ex, "Failed to load workload meetings.");
            }
        }).ConfigureAwait(false);
        ActivityText = string.Empty;
    }

    private async Task HandleSelectedMeetingChangedAsync(WorkloadMeeting? previousMeeting, WorkloadMeeting? currentMeeting, long gen)
    {
        try
        {
            if (previousMeeting != null)
            {
                await FlushPendingNotesSaveAsync(previousMeeting.Id).ConfigureAwait(false);
            }

            if (currentMeeting == null)
            {
                await ApplyMeetingSelectionAsync(null, Array.Empty<WorkloadMeetingProject>(), gen).ConfigureAwait(false);
                return;
            }

            await RunBusyAsync(async () =>
            {
                ActivityText = "Loading\u2026";
                MeetingError = null;
                try
                {
                    var projects = await _store.GetProjectsForMeetingAsync(currentMeeting.Id).ConfigureAwait(false);
                    // Apply will fence internally against stale generation — pass ours in.
                    await ApplyMeetingSelectionAsync(currentMeeting, projects, gen).ConfigureAwait(false);
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

                // Fresh user action — advance the generation so any in-flight async loads are dropped.
                var gen = System.Threading.Interlocked.Increment(ref _meetingSelectionGeneration);

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

                await ApplyMeetingSelectionAsync(newMeeting, projects, gen).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MeetingError = "Failed to create a new meeting.";
                _logger.LogError(ex, "Failed to create a new workload meeting.");
            }
        }).ConfigureAwait(false);
        ActivityText = string.Empty;
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
            catch (OperationCanceledException)
            {
                // Window closed — expected, nothing to do.
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
                // Guard against saving to a deleted meeting (debounce timer can fire after deletion).
                // Meetings is a UI-thread ObservableCollection — read via dispatcher to be thread-safe.
                var meetingExists = await _dispatcher.InvokeAsync(() => Meetings.Any(m => m.Id == meetingId));
                if (!meetingExists)
                {
                    _logger.LogWarning("Skipping notes save — meeting {MeetingId} no longer exists.", meetingId);
                    lock (_notesGate)
                    {
                        if (_pendingNotesMeetingId == meetingId)
                        {
                            _pendingNotesMeetingId = null;
                            _pendingNotesValue = null;
                        }
                    }
                    return;
                }

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

    private void OnProjectNotesChanged(WorkloadMeetingProjectRow row)
    {
        if (_suppressProjectNotesRelay) return;
        if (!IsCurrentMeeting) return;

        ScheduleProjectNotesSave(row.MeetingId, row.Wbs1, row.Notes);
    }

    /// <summary>
    /// Round 52: write path for the PM Groups grid's row-details notes editor.
    /// Routes through the same per-Wbs1 debounce as the meeting board, and
    /// mirrors the text into the matching board row so both surfaces show the
    /// same note while the window is open. Only projects that already have a
    /// WorkloadMeetingProjects row (i.e. a priority) can carry notes — the
    /// store's SaveProjectNotesAsync is an UPDATE and would silently hit zero
    /// rows otherwise; the grid disables the editor for unprioritized rows.
    /// </summary>
    public void QueueProjectNotesSaveFromUi(string wbs1, string notes)
    {
        var selection = SelectedMeeting;
        if (selection == null || !IsCurrentMeeting || string.IsNullOrWhiteSpace(wbs1)) return;

        var hasMeetingRow = CurrentProjects.Any(p => string.Equals(p.Wbs1, wbs1, StringComparison.OrdinalIgnoreCase));
        if (!hasMeetingRow) return;

        var boardRow = PriorityProjects.FirstOrDefault(p => string.Equals(p.Wbs1, wbs1, StringComparison.OrdinalIgnoreCase));
        if (boardRow != null)
        {
            // Suppress the board row's NotesChanged relay — it would schedule a
            // duplicate save of the same value through OnProjectNotesChanged.
            _suppressProjectNotesRelay = true;
            try { boardRow.Notes = notes ?? string.Empty; }
            finally { _suppressProjectNotesRelay = false; }
        }

        ScheduleProjectNotesSave(selection.Id, wbs1, notes ?? string.Empty);
    }

    private void ScheduleProjectNotesSave(Guid meetingId, string wbs1, string notes)
    {
        // Keep the canonical in-memory copy fresh so any board-row rebuild
        // (SyncMeetingPrioritiesToRows / RefreshPriorityProjects re-project
        // from CurrentProjects) carries the latest text instead of the last
        // DB read. Caller is always on the UI thread (TextBox/row setters).
        var current = CurrentProjects.FirstOrDefault(p => string.Equals(p.Wbs1, wbs1, StringComparison.OrdinalIgnoreCase));
        if (current != null) current.Notes = notes;

        ProjectNotesUpdated?.Invoke(wbs1, notes);

        var version = _projectNotesVersions.AddOrUpdate(wbs1, 1, (_, v) => v + 1);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(600), _disposeCts.Token).ConfigureAwait(false);
                if (!_projectNotesVersions.TryGetValue(wbs1, out var currentVersion) || currentVersion != version) return;
                // Guard against saving to a deleted meeting (debounce timer can fire after deletion).
                // Meetings is a UI-thread ObservableCollection — read via dispatcher.
                var meetingExists = await _dispatcher.InvokeAsync(() => Meetings.Any(m => m.Id == meetingId));
                if (!meetingExists)
                {
                    _logger.LogWarning("Skipping project notes save — meeting {MeetingId} no longer exists.", meetingId);
                    return;
                }
                await _store.SaveProjectNotesAsync(meetingId, wbs1, notes, _disposeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MeetingError = "Failed to save project notes.";
                _logger.LogError(ex, "Failed to save project notes for {Wbs1} in meeting {MeetingId}.", wbs1, meetingId);
            }
        });
    }

    // Round 52h: the board's interactive sort headers were removed with the
    // board. The priority rows (now feeding the orphan panel + export) keep a
    // stable, sensible order: priority first, then project name.
    private static System.Collections.Generic.IEnumerable<WorkloadMeetingProjectRow> ApplySortOrder(
        System.Collections.Generic.IEnumerable<WorkloadMeetingProjectRow> rows)
        => rows.OrderBy(r => r.Priority).ThenBy(r => r.ProjectName);

    public async Task ForceSaveAllAsync(CancellationToken ct = default)
    {
        var selection = SelectedMeeting;
        if (selection == null || !IsCurrentMeeting) return;

        await FlushPendingNotesSaveAsync(selection.Id).ConfigureAwait(false);

        List<WorkloadMeetingProjectRow> rows = new();
        await _dispatcher.InvokeAsync(() => rows = PriorityProjects.ToList());
        foreach (var row in rows)
            await _store.SaveProjectNotesAsync(selection.Id, row.Wbs1, row.Notes, ct).ConfigureAwait(false);
    }

    public async Task DeleteMeetingAsync()
    {
        var meeting = SelectedMeeting;
        if (meeting == null) return;

        await RunBusyAsync(async () =>
        {
            ActivityText = "Deleting\u2026";
            MeetingError = null;
            try
            {
                // Fresh user action — advance the generation so any in-flight async loads are dropped.
                var gen = System.Threading.Interlocked.Increment(ref _meetingSelectionGeneration);

                await _store.DeleteMeetingAsync(meeting.Id, _disposeCts.Token).ConfigureAwait(false);

                int nextIndex = 0;
                await _dispatcher.InvokeAsync(() =>
                {
                    var idx = Meetings.IndexOf(meeting);
                    Meetings.Remove(meeting);
                    nextIndex = Meetings.Count == 0 ? -1 : Math.Min(idx, Meetings.Count - 1);
                });

                if (nextIndex < 0)
                {
                    await ApplyMeetingSelectionAsync(null, Array.Empty<WorkloadMeetingProject>(), gen).ConfigureAwait(false);
                }
                else
                {
                    WorkloadMeeting? next = null;
                    await _dispatcher.InvokeAsync(() => next = Meetings[nextIndex]);
                    var projects = await _store.GetProjectsForMeetingAsync(next!.Id, _disposeCts.Token).ConfigureAwait(false);
                    await ApplyMeetingSelectionAsync(next, projects, gen).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                MeetingError = "Failed to delete meeting.";
                _logger.LogError(ex, "Failed to delete workload meeting {MeetingId}.", meeting.Id);
            }
        }).ConfigureAwait(false);
        ActivityText = string.Empty;
    }

    private async Task SaveMeetingAsync()
    {
        await RunBusyAsync(async () =>
        {
            ActivityText = "Saving\u2026";
            MeetingError = null;
            try
            {
                await ForceSaveAllAsync(_disposeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MeetingError = "Failed to save meeting.";
                _logger.LogError(ex, "Failed to force-save workload meeting.");
            }
        }).ConfigureAwait(false);
        ActivityText = string.Empty;
    }

    private async Task ApplyMeetingSelectionAsync(WorkloadMeeting? meeting, System.Collections.Generic.IEnumerable<WorkloadMeetingProject> projects, long gen)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            // Stale-result fence: if another selection / create / delete has advanced the generation
            // since this async load started, drop the result instead of overwriting the UI state.
            if (System.Threading.Interlocked.Read(ref _meetingSelectionGeneration) != gen) return;

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

                // Round 39b (T2.001): refresh PriorityProjects so the meeting
                // window has data on first open without the capacity window's
                // sync handler. Basic projection; capacity window enriches if
                // it's also open.
                RefreshPriorityProjects();

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}

/// <summary>
/// Round 52: one toolbar chip — "P2 ×4" in the priority colour. Colours match
/// WorkloadMeetingProjectRow.PriorityBrush / PmToolsExportService.PriorityBg.
/// </summary>
public sealed class WorkloadMeetingPriorityChip
{
    public int Priority { get; init; }
    public int Count { get; init; }
    public string Label => $"P{Priority} ×{Count}";
    public string ColorHex => Priority switch
    {
        1 => "#DC2626",
        2 => "#EA580C",
        3 => "#D97706",
        4 => "#2563EB",
        _ => "#6B7280",
    };
}
