#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Kor.Operations.Core;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.App.NetworkOps;

// One PC: what is wrong with it now, what each problem means and what has fixed it before, whether
// other PCs have it too and why, what has changed on it, and what has been wrong with it in the
// past. "Check now" asks the service to run the health probe on it and waits for the answer.
public sealed class NetworkOpsDeviceViewModel : ObservableObject
{
    /// <summary>How often a queued check is looked at; the service claims a trigger within ~5 s.</summary>
    private static readonly TimeSpan CheckPoll = TimeSpan.FromSeconds(2);

    /// <summary>A check still unanswered after this is reported as such (the probe itself takes ~10 s on a PC).</summary>
    private static readonly TimeSpan CheckGiveUp = TimeSpan.FromMinutes(3);

    private readonly NetworkOpsClient _client;
    private FleetSnapshot _snapshot;
    private DeviceRow _device;
    private IReadOnlyList<Resolution> _resolutions = [];
    private FindingRow? _selectedFinding;
    private string _statusMessage = "Ready.";
    private string _checkStatus = string.Empty;
    private bool _isChecking;
    private string _newNoteText = string.Empty;
    private string _actionNote = string.Empty;

    public NetworkOpsDeviceViewModel(NetworkOpsClient client, FleetSnapshot snapshot, DeviceRow device)
    {
        _client = client;
        _snapshot = snapshot;
        _device = device;
        ApplySnapshot(DateTime.UtcNow);
    }

    public string DeviceName => _device.Name;

    /// <summary>A rack device (host, storage, UPS, backup, network, internet) rather than a PC.</summary>
    public bool IsRack => Kor.Operations.NetworkOps.Core.Rack.RackKinds.IsRack(_device.Kind);
    public string CheckButtonText => IsRack ? "Read this device now" : "Check this PC now";
    public string CheckButtonTip => IsRack
        ? "Re-reads this device through the rack sweep (read-only), then reloads this page. A few seconds to a minute."
        : "Runs the health probe on this PC through the service, then reloads this page. About 10-40 seconds.";
    public string OthersHeading => IsRack ? "Other devices" : "Other PCs";
    public bool ShowsPatterns => !IsRack;

    public string StateLabel { get; private set; } = string.Empty;
    public Brush StateBrush { get; private set; } = NetworkOpsBrushes.Unknown;
    public string IdentityLine { get; private set; } = string.Empty;
    public string HardwareLine { get; private set; } = string.Empty;
    public string FreshnessLine { get; private set; } = string.Empty;

    public ObservableCollection<FindingRow> OpenFindings { get; } = new();
    public ObservableCollection<ClearedRow> Cleared { get; } = new();
    public ObservableCollection<ChangeRow> Changes { get; } = new();
    public ObservableCollection<NoteLine> Notes { get; } = new();

    // ---- the selected finding, explained
    public string Meaning { get; private set; } = string.Empty;
    public IReadOnlyList<string> Causes { get; private set; } = [];
    public IReadOnlyList<string> KnownFixes { get; private set; } = [];
    public IReadOnlyList<string> LearnedFixes { get; private set; } = [];
    public string IfIgnored { get; private set; } = string.Empty;
    public string ElsewhereText { get; private set; } = string.Empty;
    public IReadOnlyList<string> PatternTexts { get; private set; } = [];
    public bool HasSelection => _selectedFinding is not null;
    public bool SelectedIsQuiet => _selectedFinding?.Finding.IsQuiet(DateTime.UtcNow) == true;

    public FindingRow? SelectedFinding
    {
        get => _selectedFinding;
        set
        {
            if (SetField(ref _selectedFinding, value))
                Explain();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string CheckStatus
    {
        get => _checkStatus;
        private set => SetField(ref _checkStatus, value);
    }

    private bool IsChecking
    {
        get => _isChecking;
        set
        {
            if (SetField(ref _isChecking, value))
                OnPropertyChanged(nameof(CanCheck));
        }
    }

    public bool CanCheck => !_isChecking;

    public string NewNoteText
    {
        get => _newNoteText;
        set => SetField(ref _newNoteText, value ?? string.Empty);
    }

    /// <summary>Optional reason saved with an acknowledge or snooze ("waiting on the new drive").</summary>
    public string ActionNote
    {
        get => _actionNote;
        set => SetField(ref _actionNote, value ?? string.Empty);
    }

    // ------------------------------------------------------------------ loading

    public async Task LoadHistoryAsync(CancellationToken ct)
    {
        try
        {
            var history = await _client.GetDeviceHistoryAsync(_device.DeviceId, ct).ConfigureAwait(true);
            _resolutions = await _client.GetResolutionsAsync(ct).ConfigureAwait(true);
            ApplyHistory(history);
            Explain();
            StatusMessage = $"Loaded at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"History failed to load: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task ReloadAsync(CancellationToken ct)
    {
        _snapshot = IsRack ? await _client.GetRackAsync(ct).ConfigureAwait(true) : await _client.GetFleetAsync(ct).ConfigureAwait(true);
        _device = _snapshot.Devices.FirstOrDefault(d => d.DeviceId == _device.DeviceId) ?? _device;
        ApplySnapshot(DateTime.UtcNow);
        await LoadHistoryAsync(ct).ConfigureAwait(true);
    }

    /// <summary>Internal so tests can drive the page from a fixture snapshot without the service.</summary>
    internal void ApplySnapshot(DateTime nowUtc)
    {
        var facts = _snapshot.FactsOf(_device.Name);
        string F(string key) => facts.TryGetValue(key, out var v) ? v : "";
        var open = _snapshot.OpenOn(_device.Name).ToList();
        var state = DeviceStatus.Of(_device.LastCheckedUtc is not null, open.Select(f => f.AsView()), nowUtc);

        StateLabel = NetworkOpsText.Label(state);
        StateBrush = NetworkOpsBrushes.For(state);
        if (IsRack)
        {
            // What it is, what it runs, and what it is doing right now.
            IdentityLine = Join(" · ", _device.Kind, F(Facts.Model), F("esxi.version"), F("dsm.version"), string.IsNullOrEmpty(F("fw.version")) ? "" : $"firmware {F("fw.version")}",
                string.IsNullOrEmpty(F("wan.public-ip")) ? "" : $"public IP {F("wan.public-ip")}", F("unifi.site"));
            HardwareLine = Join(" · ", _device.Summary ?? "", string.IsNullOrEmpty(F("hw.serial")) ? "" : $"S/N {F("hw.serial")}");
            var stale = _device.LastCheckedUtc is not { } lc || nowUtc - lc > TimeSpan.FromMinutes(15);
            FreshnessLine = $"Read every 5 minutes · last good read {CommandCenterView.Ago(_device.LastCheckedUtc, nowUtc)}"
                + (stale ? " · STALE: it has not answered recently" : "");
        }
        else
        {
            IdentityLine = Join(" · ", F(Facts.Model), string.IsNullOrEmpty(F(Facts.OsRelease)) ? "" : $"Windows {F(Facts.OsRelease)} ({F(Facts.OsBuild)})");
            HardwareLine = Join(" · ", F(Facts.Cpu), string.IsNullOrEmpty(F(Facts.RamGb)) ? "" : $"{F(Facts.RamGb)} GB RAM",
                string.IsNullOrEmpty(F(Facts.GpuName)) ? "" : $"{F(Facts.GpuName)} (driver {F(Facts.GpuDriver)})", string.IsNullOrEmpty(F(Facts.Bios)) ? "" : $"BIOS {F(Facts.Bios)}");
            FreshnessLine = $"Last checked {CommandCenterView.Ago(_device.LastCheckedUtc, nowUtc)} · last answered the network {CommandCenterView.Ago(_device.LastReachableUtc, nowUtc)}"
                + (CommandCenterView.FreshnessOf(_device.LastCheckedUtc, nowUtc) == Freshness.Stale ? " · STALE: what is shown here may be out of date" : "");
        }

        var selectedId = _selectedFinding?.Finding.FindingId;
        OpenFindings.Clear();
        foreach (var f in open.OrderBy(f => f.IsQuiet(nowUtc)).ThenByDescending(f => f.Severity).ThenBy(f => f.FirstSeenUtc))
            OpenFindings.Add(new FindingRow
            {
                Finding = f,
                SinceText = $"since {NetworkOpsText.When(f.FirstSeenUtc)}",
                QuietText = f.AcknowledgedUtc is not null ? $"Acknowledged by {f.AcknowledgedBy}{(f.AckNote is null ? "" : $": {f.AckNote}")}"
                          : f.SnoozedUntilUtc > nowUtc ? $"Snoozed until {NetworkOpsText.When(f.SnoozedUntilUtc.Value)}{(f.AckNote is null ? "" : $": {f.AckNote}")}"
                          : "",
            });
        _selectedFinding = OpenFindings.FirstOrDefault(r => r.Finding.FindingId == selectedId) ?? OpenFindings.FirstOrDefault();

        foreach (var name in new[] { nameof(StateLabel), nameof(StateBrush), nameof(IdentityLine), nameof(HardwareLine), nameof(FreshnessLine), nameof(SelectedFinding) })
            OnPropertyChanged(name);
        Explain();
    }

    internal void ApplyHistory(DeviceHistory history)
    {
        Cleared.Clear();
        foreach (var c in history.Cleared)
            Cleared.Add(new ClearedRow
            {
                Title = c.Title,
                WhenText = $"{NetworkOpsText.When(c.FirstSeenUtc)} → {NetworkOpsText.When(c.ClearedUtc)}",
                How = c.Resolution ?? "",
            });

        Changes.Clear();
        foreach (var c in CommandCenterView.ChangesFrom(history.Facts.Select(f => (f.Fact, f.Value, f.FirstSeenUtc, f.SupersededUtc)).ToList()))
            Changes.Add(new ChangeRow { WhenText = NetworkOpsText.When(c.AtUtc), Description = c.Description });

        Notes.Clear();
        foreach (var n in history.Notes)
            Notes.Add(new NoteLine { Header = $"{n.Author} · {NetworkOpsText.When(n.CreatedUtc)}", Body = n.Body });
    }

    private void Explain()
    {
        var f = _selectedFinding?.Finding;
        var k = f is null ? null : Knowledge.For(f.RuleKey);
        Meaning = f is null ? "" : k?.Meaning ?? "No explanation written for this kind of finding yet.";
        Causes = k?.Causes ?? [];
        KnownFixes = k?.Fixes ?? [];
        IfIgnored = k?.IfIgnored ?? "";
        List<string> learned = f is null ? [] : CommandCenterView.LearnedFixesFor(f.RuleKey, _resolutions).Select(x => x.Summary).ToList();
        LearnedFixes = f is null || learned.Count > 0 ? learned
            : ["Nothing learned yet. A change is offered here once it has coincided with this problem clearing at least twice."];

        if (f is null)
        {
            ElsewhereText = "";
            PatternTexts = [];
        }
        else
        {
            var others = CommandCenterView.SameProblemElsewhere(f, _snapshot.OpenFindings);
            var what = IsRack ? "device" : "PC";
            ElsewhereText = others.Count == 0 ? $"Only this {what} has it right now." : $"Also open on {others.Count} other {what}(s): {string.Join(", ", others)}";
            var patterns = CommandCenterView.PatternsFor(f.RuleKey, _snapshot.FactsOf(_device.Name), _snapshot.Patterns).Select(p => p.Summary).ToList();
            PatternTexts = patterns.Count > 0 ? patterns
                : IsRack ? ["Not applicable: fleet patterns compare PCs."]
                : ["None: nothing this PC has in common with the others that have it stands out."];
        }

        foreach (var name in new[] { nameof(Meaning), nameof(Causes), nameof(KnownFixes), nameof(IfIgnored), nameof(LearnedFixes),
                                     nameof(ElsewhereText), nameof(PatternTexts), nameof(HasSelection), nameof(SelectedIsQuiet) })
            OnPropertyChanged(name);
    }

    // ------------------------------------------------------------------ actions

    /// <summary>Queues a health check of this PC and waits for the service to answer. Returns when it has, or given up.</summary>
    public async Task CheckNowAsync(CancellationToken ct)
    {
        if (IsChecking) return;
        IsChecking = true;
        try
        {
            var id = await _client.QueueCheckAsync(_device.Name, ct).ConfigureAwait(true);
            CheckStatus = "Queued — waiting for the service to pick it up...";
            var started = DateTime.UtcNow;
            while (true)
            {
                await Task.Delay(CheckPoll, ct).ConfigureAwait(true);
                var t = await _client.GetTriggerAsync(id, ct).ConfigureAwait(true);
                if (t is null) { CheckStatus = "The check request has disappeared from the queue."; return; }
                var waited = (int)(DateTime.UtcNow - started).TotalSeconds;
                switch (t.Status)
                {
                    case "Pending":
                        CheckStatus = $"Queued {waited}s — waiting for the service to pick it up...";
                        break;
                    case "Running":
                        CheckStatus = $"Checking {_device.Name} ({waited}s)...";
                        break;
                    case "Done":
                        CheckStatus = $"Checked in {waited}s: {t.Result}";
                        await ReloadAsync(ct).ConfigureAwait(true);
                        return;
                    default:
                        CheckStatus = $"Check {t.Status.ToLowerInvariant()}: {t.Result}";
                        return;
                }
                if (DateTime.UtcNow - started > CheckGiveUp)
                {
                    var cancelled = t.Status == "Pending" && await _client.CancelCheckAsync(id, ct).ConfigureAwait(true);
                    CheckStatus = cancelled
                        ? "No answer in 3 minutes: the service never picked the check up, so it was withdrawn. Is the service running?"
                        : "No answer in 3 minutes: the check is still running on the service. The page will show the result on the next refresh.";
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CheckStatus = $"Check failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    public Task AcknowledgeAsync(CancellationToken ct)
        => Annotate("Acknowledged", f => _client.AcknowledgeAsync(f.FindingId, ActionNote, ct), ct);

    public Task SnoozeAsync(TimeSpan duration, CancellationToken ct)
        => Annotate($"Snoozed for {(duration.TotalDays >= 1 ? $"{duration.TotalDays:0} day(s)" : $"{duration.TotalHours:0} hour(s)")}",
            f => _client.SnoozeAsync(f.FindingId, DateTime.UtcNow + duration, ActionNote, ct), ct);

    public Task ReopenAsync(CancellationToken ct)
        => Annotate("Reopened", f => _client.ReopenAsync(f.FindingId, ct), ct);

    private async Task Annotate(string verb, Func<FleetFinding, Task> write, CancellationToken ct)
    {
        if (_selectedFinding?.Finding is not { } f) return;
        try
        {
            await write(f).ConfigureAwait(true);
            ActionNote = string.Empty;
            await ReloadAsync(ct).ConfigureAwait(true);
            // After the reload, which reports its own "Loaded at": the confirmation is what Ian needs to see.
            StatusMessage = $"{verb}: {f.Title}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"{verb} failed: {ex.Message}";
        }
    }

    public async Task AddNoteAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(NewNoteText)) return;
        try
        {
            await _client.AddNoteAsync(_device.DeviceId, NewNoteText, ct).ConfigureAwait(true);
            NewNoteText = string.Empty;
            await LoadHistoryAsync(ct).ConfigureAwait(true);
            StatusMessage = "Note added.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Note failed: {ex.Message}";
        }
    }

    private static string Join(string sep, params string[] parts) => string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
