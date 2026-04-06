#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Kor.Operations.PMTools
{
    public sealed class DayProjectEntry
    {
        public string Wbs1 { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public double Hours { get; set; }
        public bool IsOverhead => Wbs1.StartsWith("99999", StringComparison.Ordinal);
    }

    public sealed class CalendarDayCell : INotifyPropertyChanged
    {
        private DateTime _date;
        private string _dayLabel = "";
        private double _totalHours;
        private bool _hasOverhead;
        private bool _isEmpty = true;
        private bool _isOutsideMonth;
        private List<DayProjectEntry> _projects = new();
        private List<BarSegment> _segments = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public DateTime Date
        {
            get => _date;
            set
            {
                _date = value;
                OnPropertyChanged();
            }
        }

        public string DayLabel
        {
            get => _dayLabel;
            set
            {
                _dayLabel = value;
                OnPropertyChanged();
            }
        }

        public double TotalHours
        {
            get => _totalHours;
            set
            {
                _totalHours = value;
                OnPropertyChanged();
            }
        }

        public bool HasOverhead
        {
            get => _hasOverhead;
            set
            {
                _hasOverhead = value;
                OnPropertyChanged();
            }
        }

        public bool IsEmpty
        {
            get => _isEmpty;
            set
            {
                _isEmpty = value;
                OnPropertyChanged();
            }
        }

        public bool IsOutsideMonth
        {
            get => _isOutsideMonth;
            set
            {
                _isOutsideMonth = value;
                OnPropertyChanged();
            }
        }

        public List<DayProjectEntry> Projects
        {
            get => _projects;
            set
            {
                _projects = value;
                OnPropertyChanged();
            }
        }

        public List<BarSegment> Segments
        {
            get => _segments;
            set
            {
                _segments = value;
                OnPropertyChanged();
            }
        }

        public string OverflowLabel { get; set; } = "";
        public string OverflowTooltip { get; set; } = "";
        public bool IsWeekend { get; set; }
        public double BillablePct { get; set; }
        public string BillablePctLabel { get; set; } = "";
        public double BillableBarWidth { get; set; }
        public double UnbillableBarWidth { get; set; }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class BarSegment
    {
        public double Proportion { get; set; }
        public string Wbs1 { get; set; } = "";
        public string TooltipText { get; set; } = "";
        public bool IsOverhead { get; set; }
        public double WidthPx { get; set; }
        public string ShortLabel { get; set; } = "";
    }
}
