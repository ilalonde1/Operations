#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Kor.Operations.App.PMTools;

public sealed class WorkloadMeetingProjectRow : INotifyPropertyChanged
{
    private string _notes = string.Empty;

    public Guid MeetingId { get; set; }
    public string Wbs1 { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string PmName { get; set; } = string.Empty;

    /// <summary>Spec R2/R4: the project's next deadline from Deltek Projects_Deadlines, or null
    /// when none is dated today or later. Set by WorkloadMeetingPanelViewModel.</summary>
    public Kor.Operations.Financials.ProjectDeadline? NextDeadline { get; set; }

    public bool HasNextDeadline => NextDeadline is not null;

    public string NextDeadlineDateText => NextDeadline?.Date.ToString("ddd d MMM") ?? "";

    /// <summary>"Progress - 60% Progress": the issue type and the PM's remark, whichever exist.</summary>
    public string NextDeadlineWhat
    {
        get
        {
            if (NextDeadline is null) return "";
            var issue = NextDeadline.Issue;
            var remarks = NextDeadline.Remarks;
            if (issue.Length == 0) return remarks;
            if (remarks.Length == 0) return issue;
            return issue + " - " + remarks;
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            var v = value ?? string.Empty;
            if (_notes == v) return;
            _notes = v;
            OnPropertyChanged();
            NotesChanged?.Invoke(this);
        }
    }

    public string PriorityLabel => Priority switch
    {
        1 => "P1", 2 => "P2", 3 => "P3", 4 => "P4", 5 => "P5", _ => ""
    };

    public Brush PriorityBrush => Priority switch
    {
        1 => new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
        2 => new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
        3 => new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)),
        4 => new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
        5 => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
        _ => Brushes.Transparent
    };

    public event Action<WorkloadMeetingProjectRow>? NotesChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
