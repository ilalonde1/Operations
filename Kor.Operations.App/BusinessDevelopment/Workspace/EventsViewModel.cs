#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.IndustryEvents;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class EventsViewModel : INotifyPropertyChanged
{
    private readonly IIndustryEventStore _store;
    private string _statusMessage = "Ready.";

    public EventsViewModel(IIndustryEventStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<EventRow> Events { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            StatusMessage = "Loading industry events...";
            var rows = await _store.ListUpcomingAsync(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            var projected = rows
                .Select(ToEventRow)
                .OrderBy(r => r.TierRank)
                .ThenBy(r => r.StartDateSort)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Events.Clear();
            foreach (var row in projected)
            {
                ct.ThrowIfCancellationRequested();
                Events.Add(row);
            }

            StatusMessage = $"Loaded {Events.Count:N0} upcoming events.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Events load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static EventRow ToEventRow(IndustryEventRow row)
    {
        var score = 0;
        if (ContainsAny(row.Market, "bc", "british columbia", "vancouver", "island", "okanagan", "lower mainland", "alberta", "edmonton", "calgary", "la", "socal", "pacific northwest", "washington", "oregon", "cascadia"))
        {
            score++;
        }

        if (ContainsAny($"{row.SectorsThemes} {row.Name}", "architecture", "structural", "engineering", "timber", "mass-timber", "seismic", "health", "education", "school", "housing", "institutional", "civic", "recreation"))
        {
            score++;
        }

        if (!string.IsNullOrWhiteSpace(row.TargetsPresent))
        {
            score += 2;
        }

        if (IsPriorityEventType(row.EventType))
        {
            score++;
        }

        var tier = score >= 4 ? "Tier 1" : score >= 2 ? "Tier 2" : "Watch";
        var tierRank = tier switch
        {
            "Tier 1" => 0,
            "Tier 2" => 1,
            _ => 2,
        };

        return new EventRow(
            tier,
            tierRank,
            row.Name ?? "(unnamed)",
            row.Organizer,
            FormatDates(row.StartDate, row.EndDate),
            row.StartDate ?? DateOnly.MaxValue,
            Location(row.City, row.Market),
            row.EventType,
            row.SectorsThemes,
            row.TargetsPresent,
            row.KorRelevance,
            row.RegistrationUrl,
            row.CostNote,
            row.SourceNote);
    }

    private static bool IsPriorityEventType(string? eventType)
    {
        var normalized = eventType?.Trim();
        return string.Equals(normalized, "awards-gala", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "conference", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "association-meeting", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string? value, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatDates(DateOnly? startDate, DateOnly? endDate)
    {
        if (!startDate.HasValue)
        {
            return "";
        }

        var start = startDate.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        if (!endDate.HasValue || endDate.Value == startDate.Value)
        {
            return start;
        }

        return $"{start} - {endDate.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";
    }

    private static string Location(string? city, string? market)
        => string.Join(" / ", new[] { city, market }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));

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

    public sealed record EventRow(
        string Tier,
        int TierRank,
        string Name,
        string? Organizer,
        string DateDisplay,
        DateOnly StartDateSort,
        string Location,
        string? EventType,
        string? SectorsThemes,
        string? TargetsPresent,
        string? KorRelevance,
        string? RegistrationUrl,
        string? CostNote,
        string? SourceNote);
}
