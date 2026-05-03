#nullable enable
using System;
using System.Windows.Media;
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Flat row view for the Ingestion Runs panel. Shows one row per
/// <c>opportunities.IngestionRuns</c> entry with display-friendly fields
/// and a colour-coded status pill.
/// </summary>
public sealed class IngestionRunRowView
{
    public IngestionRunRowView(IngestionRun model)
    {
        Model = model;
    }

    public IngestionRun Model { get; }

    public Guid Id => Model.Id;
    public string ProviderName => Model.ProviderName;

    public string StartedDisplay => Model.StartedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    public string EndedDisplay =>
        Model.EndedAtUtc?.LocalDateTime.ToString("HH:mm:ss") ?? "in progress";

    public string DurationDisplay
    {
        get
        {
            if (!Model.EndedAtUtc.HasValue)
            {
                return "—";
            }

            var d = Model.EndedAtUtc.Value - Model.StartedAtUtc;
            return d.TotalSeconds < 90
                ? $"{d.TotalSeconds:F0}s"
                : d.TotalMinutes < 90 ? $"{d.TotalMinutes:F1}m"
                : $"{d.TotalHours:F1}h";
        }
    }

    public string CountsDisplay =>
        $"{Model.InsertedCount} new / {Model.DuplicateCount} dup / {Model.SkippedCount} skip / {Model.FailedCount} fail";

    public string StatusDisplay
    {
        get
        {
            if (!Model.EndedAtUtc.HasValue)
            {
                return "Running";
            }

            return Model.Success ? "OK" : "Failed";
        }
    }

    public Brush StatusBrush
    {
        get
        {
            if (!Model.EndedAtUtc.HasValue)
            {
                return RunningBrush;
            }

            return Model.Success ? OkBrush : FailedBrush;
        }
    }

    public string ErrorSummary => Model.ErrorSummary ?? string.Empty;

    private static readonly Brush OkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22)));
    private static readonly Brush FailedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E)));
    private static readonly Brush RunningBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00)));

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
