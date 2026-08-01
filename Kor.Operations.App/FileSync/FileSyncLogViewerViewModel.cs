#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;

namespace Kor.Operations.App.FileSync;

// Backs FileSyncLogViewerWindow. Owns the host/date/level/job filters, the
// tailer, and the displayed line collection. The window itself is just glue
// for the auto-refresh timer and the right-click context menu.
public sealed class FileSyncLogViewerViewModel : ObservableObject
{
    // Hard cap so a 10 MB log file doesn't blow up the DataGrid. Most recent
    // wins; older entries roll off when the cap is exceeded.
    private const int MaxDisplayedLines = 5000;

    private readonly FileSyncControlPlaneReader _reader;
    private readonly FileSyncLogTailer _tailer;
    private readonly List<FileSyncLogLine> _all = new();

    private string? _selectedHost;
    private DateTime _selectedDate = DateTime.Today;
    private string _minLevel = "INF";
    private string? _jobFilter;
    private string _searchText = string.Empty;
    private bool _autoRefresh = true;
    private bool _autoScroll = true;
    private string _statusMessage = "Ready.";
    private FileSyncLogLine? _selectedLine;
    private DateTimeOffset? _fromTime;
    private DateTimeOffset? _toTime;

    public FileSyncLogViewerViewModel(
        FileSyncControlPlaneReader reader,
        string? initialHost = null,
        string? initialJobFilter = null,
        DateTimeOffset? initialFrom = null,
        DateTimeOffset? initialTo = null)
    {
        _reader = reader;
        _selectedHost = initialHost;
        _jobFilter = initialJobFilter;
        _fromTime = initialFrom;
        _toTime = initialTo;
        if (initialFrom.HasValue) _selectedDate = initialFrom.Value.LocalDateTime.Date;
        _tailer = new FileSyncLogTailer(ResolveLogPath());
    }

    public ObservableCollection<string> Hosts { get; } = new();

    public ObservableCollection<string> Jobs { get; } = new() { AllJobsLabel };

    public ObservableCollection<FileSyncLogLine> DisplayedLines { get; } = new();

    public string? SelectedHost
    {
        get => _selectedHost;
        set
        {
            if (SetField(ref _selectedHost, value))
            {
                _tailer.Retarget(ResolveLogPath());
                _all.Clear();
                ApplyFilters();
            }
        }
    }

    public DateTime SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (SetField(ref _selectedDate, value.Date))
            {
                _tailer.Retarget(ResolveLogPath());
                _all.Clear();
                ApplyFilters();
            }
        }
    }

    // 3-letter Serilog level. UI shows ALL/VRB/DBG/INF/WRN/ERR/FTL.
    public string MinLevel
    {
        get => _minLevel;
        set { if (SetField(ref _minLevel, value)) ApplyFilters(); }
    }

    public string? JobFilter
    {
        get => _jobFilter;
        set
        {
            if (SetField(ref _jobFilter, value))
            {
                OnPropertyChanged(nameof(HasJobFilter));
                OnPropertyChanged(nameof(JobFilterPillText));
                OnPropertyChanged(nameof(HasAnyFilter));
                ApplyFilters();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasSearchText));
                OnPropertyChanged(nameof(SearchPillText));
                OnPropertyChanged(nameof(HasAnyFilter));
                ApplyFilters();
            }
        }
    }

    // The pill strip in the window binds these directly so we don't need a
    // separate ObservableCollection; visibility per pill is driven by Has*.
    public bool HasJobFilter => !string.IsNullOrEmpty(_jobFilter) && _jobFilter != AllJobsLabel;

    public bool HasSearchText => _searchText.Length > 0;

    public string JobFilterPillText => $"Job: {_jobFilter}";

    public string SearchPillText => $"Search: \"{_searchText}\"";

    public string TimeWindowPillText
    {
        get
        {
            string from = _fromTime.HasValue ? _fromTime.Value.LocalDateTime.ToString("HH:mm:ss") : "*";
            string to   = _toTime.HasValue   ? _toTime.Value.LocalDateTime.ToString("HH:mm:ss")   : "*";
            return $"Time: {from} – {to}";
        }
    }

    public bool AutoRefresh
    {
        get => _autoRefresh;
        set => SetField(ref _autoRefresh, value);
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetField(ref _autoScroll, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public FileSyncLogLine? SelectedLine
    {
        get => _selectedLine;
        set => SetField(ref _selectedLine, value);
    }

    // Optional time-window filter. When either is non-null, ApplyFilters
    public bool HasTimeWindow => _fromTime.HasValue || _toTime.HasValue;

    public string CurrentLogPath => _tailer.Path;

    public const string AllJobsLabel = "(all)";

    public async Task InitializeAsync(CancellationToken ct)
    {
        // Populate hosts from heartbeat table; populate jobs from Jobs table.
        try
        {
            var hosts = await _reader.GetHeartbeatsAsync(ct).ConfigureAwait(true);
            Hosts.Clear();
            foreach (var h in hosts) Hosts.Add(h.HostName);
            if (string.IsNullOrEmpty(_selectedHost) && Hosts.Count > 0)
                SelectedHost = Hosts[0];

            var jobs = await _reader.GetJobsAsync(ct).ConfigureAwait(true);
            Jobs.Clear();
            Jobs.Add(AllJobsLabel);
            foreach (var j in jobs) Jobs.Add(j.JobName);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Initial load failed: {ex.GetType().Name}: {ex.Message}";
        }

        await PollOnceAsync(ct).ConfigureAwait(true);
    }

    public async Task PollOnceAsync(CancellationToken ct)
    {
        try
        {
            var newLines = await _tailer.ReadNewAsync(ct).ConfigureAwait(true);
            if (newLines.Count == 0)
            {
                StatusMessage = $"Up to date (no new lines) | {DateTime.Now:HH:mm:ss} | {_tailer.Path}";
                return;
            }

            _all.AddRange(newLines);
            // Trim the underlying buffer too -- otherwise long sessions accumulate
            // every line ever read from a 10 MB file.
            if (_all.Count > MaxDisplayedLines * 2)
                _all.RemoveRange(0, _all.Count - MaxDisplayedLines * 2);

            ApplyFilters();
            StatusMessage = $"+{newLines.Count} new line(s) | {DateTime.Now:HH:mm:ss} | {_tailer.Path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Read failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void ApplyFilters()
    {
        var minSev = MinLevelToSeverity(_minLevel);
        var search = _searchText.Trim();
        var jobFilter = string.IsNullOrEmpty(_jobFilter) || _jobFilter == AllJobsLabel ? null : _jobFilter;

        IEnumerable<FileSyncLogLine> q = _all;
        if (minSev > 0)
            q = q.Where(l => l.Severity >= minSev);
        if (jobFilter is not null)
            q = q.Where(l => l.SourceContext.IndexOf(jobFilter, StringComparison.OrdinalIgnoreCase) >= 0
                          || l.Message.IndexOf(jobFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        if (search.Length > 0)
            q = q.Where(l => l.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                          || l.Details.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        if (_fromTime.HasValue) q = q.Where(l => l.Timestamp >= _fromTime.Value);
        if (_toTime.HasValue)   q = q.Where(l => l.Timestamp <= _toTime.Value);

        var keep = q.TakeLast(MaxDisplayedLines).ToList();
        DisplayedLines.Clear();
        foreach (var l in keep) DisplayedLines.Add(l);
    }

    public void ClearTimeWindow()
    {
        _fromTime = null;
        _toTime = null;
        OnPropertyChanged(nameof(HasTimeWindow));
        OnPropertyChanged(nameof(TimeWindowPillText));
        OnPropertyChanged(nameof(HasAnyFilter));
        ApplyFilters();
    }

    public bool HasAnyFilter => HasJobFilter || HasSearchText || HasTimeWindow;

    private static int MinLevelToSeverity(string level) => level switch
    {
        "ALL" => 0,
        "VRB" => 0,
        "DBG" => 1,
        "INF" => 2,
        "WRN" => 3,
        "ERR" => 4,
        "FTL" => 5,
        _ => 2,
    };

    // \\<host>\C$\ProgramData\KorOperations\FileSync\logs\filesync-YYYYMMDD.log
    // when host != local; local C:\ProgramData\... when host == local.
    public string ResolveLogPath()
    {
        var host = _selectedHost ?? Environment.MachineName;
        var fileName = $"filesync-{_selectedDate:yyyyMMdd}.log";

        if (string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KorOperations", "FileSync", "logs", fileName);
        }

        // Admin share. Will only resolve if the operator has admin rights on
        // the target host -- documented in the Status bar on read failure.
        return $@"\\{host}\C$\ProgramData\KorOperations\FileSync\logs\{fileName}";
    }
}
