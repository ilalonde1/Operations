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
