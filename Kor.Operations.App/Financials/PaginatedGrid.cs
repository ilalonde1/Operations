#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Kor.Operations.Financials;

internal sealed class PaginatedGrid<TSource, TRow>
{
    private IReadOnlyList<TSource> _allRows = Array.Empty<TSource>();
    private int _pageIndex;
    private readonly int _pageSize;
    private readonly Func<IEnumerable<TSource>, IEnumerable<TSource>> _sort;
    private readonly Func<TSource, TRow> _project;
    private readonly Action _notifyExtras;
    private readonly Action<string> _notify;
    private readonly string _pagedRowsPropertyName;
    private readonly string _canGoPrevPropertyName;
    private readonly string _canGoNextPropertyName;
    private readonly string _pageTextPropertyName;

    public PaginatedGrid(
        int pageSize,
        Func<IEnumerable<TSource>, IEnumerable<TSource>> sort,
        Func<TSource, TRow> project,
        Action<string> notify,
        Action notifyExtras,
        string pagedRowsPropertyName,
        string canGoPrevPropertyName,
        string canGoNextPropertyName,
        string pageTextPropertyName)
    {
        _pageSize = pageSize;
        _sort = sort ?? throw new ArgumentNullException(nameof(sort));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _notify = notify ?? throw new ArgumentNullException(nameof(notify));
        _notifyExtras = notifyExtras ?? throw new ArgumentNullException(nameof(notifyExtras));
        _pagedRowsPropertyName = pagedRowsPropertyName ?? throw new ArgumentNullException(nameof(pagedRowsPropertyName));
        _canGoPrevPropertyName = canGoPrevPropertyName ?? throw new ArgumentNullException(nameof(canGoPrevPropertyName));
        _canGoNextPropertyName = canGoNextPropertyName ?? throw new ArgumentNullException(nameof(canGoNextPropertyName));
        _pageTextPropertyName = pageTextPropertyName ?? throw new ArgumentNullException(nameof(pageTextPropertyName));
    }

    public ObservableCollection<TRow> PagedRows { get; } = new();

    public IReadOnlyList<TSource> AllRows => _allRows;

    public int PageIndex => _pageIndex;

    public int RowCount => _allRows.Count;

    public int PageCount => _allRows.Count == 0
        ? 1
        : (int)Math.Ceiling(_allRows.Count / (double)_pageSize);

    public bool CanGoPrev => _pageIndex > 0;

    public bool CanGoNext => _pageIndex < PageCount - 1;

    public string PageText => _allRows.Count == 0 ? string.Empty : $"Page {_pageIndex + 1:N0} of {PageCount:N0}";

    public bool HasRows => _allRows.Count > 0;

    public void Load(IReadOnlyList<TSource> allRows)
    {
        _allRows = allRows ?? Array.Empty<TSource>();
        _pageIndex = 0;
        Rebuild();
    }

    public void Rebuild()
    {
        PagedRows.Clear();

        foreach (var row in _sort(_allRows)
                     .Skip(_pageIndex * _pageSize)
                     .Take(_pageSize)
                     .Select(_project))
        {
            PagedRows.Add(row);
        }

        _notify(_pagedRowsPropertyName);
        _notify(_canGoPrevPropertyName);
        _notify(_canGoNextPropertyName);
        _notify(_pageTextPropertyName);
        _notifyExtras();
    }

    public void NextPage()
    {
        if (!CanGoNext)
            return;

        _pageIndex++;
        Rebuild();
    }

    public void PrevPage()
    {
        if (!CanGoPrev)
            return;

        _pageIndex--;
        Rebuild();
    }
}

internal sealed class UnpagedGrid<TSource, TRow>
{
    private IReadOnlyList<TSource> _allRows = Array.Empty<TSource>();
    private readonly Func<IEnumerable<TSource>, IEnumerable<TSource>> _sort;
    private readonly Func<TSource, TRow> _project;
    private readonly Action<string> _notify;
    private readonly Action _notifyExtras;
    private readonly string _pagedRowsPropertyName;

    public UnpagedGrid(
        Func<IEnumerable<TSource>, IEnumerable<TSource>> sort,
        Func<TSource, TRow> project,
        Action<string> notify,
        Action notifyExtras,
        string pagedRowsPropertyName)
    {
        _sort = sort ?? throw new ArgumentNullException(nameof(sort));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _notify = notify ?? throw new ArgumentNullException(nameof(notify));
        _notifyExtras = notifyExtras ?? throw new ArgumentNullException(nameof(notifyExtras));
        _pagedRowsPropertyName = pagedRowsPropertyName ?? throw new ArgumentNullException(nameof(pagedRowsPropertyName));
    }

    public ObservableCollection<TRow> PagedRows { get; } = new();

    public IReadOnlyList<TSource> AllRows => _allRows;

    public int RowCount => _allRows.Count;

    public bool HasRows => _allRows.Count > 0;

    public void Load(IReadOnlyList<TSource> allRows)
    {
        _allRows = allRows ?? Array.Empty<TSource>();
        PagedRows.Clear();

        foreach (var row in _sort(_allRows).Select(_project))
        {
            PagedRows.Add(row);
        }

        _notify(_pagedRowsPropertyName);
        _notifyExtras();
    }
}
