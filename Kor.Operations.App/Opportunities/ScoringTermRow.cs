#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Editable row inside the scoring-profile DataGrids. Bound to one entry from
/// <c>ScoringOptions.PositiveTermWeights</c> / Negative / Region. Held in an
/// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/> so the
/// grid can add / remove rows without rebinding.
/// </summary>
public sealed class ScoringTermRow : INotifyPropertyChanged
{
    private string _term = string.Empty;
    private decimal _weight;

    public ScoringTermRow()
    {
    }

    public ScoringTermRow(string term, decimal weight)
    {
        _term = term;
        _weight = weight;
    }

    public string Term
    {
        get => _term;
        set
        {
            if (!string.Equals(_term, value, StringComparison.Ordinal))
            {
                _term = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    public decimal Weight
    {
        get => _weight;
        set
        {
            if (_weight != value)
            {
                _weight = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
