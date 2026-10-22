using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Kor.Operations
{
    /// <summary>
    /// Holds every fixed-width DataGrid column at the width its XAML gives it. Short of room, WPF's DataGrid takes width
    /// from fixed columns first, down to their MinWidth, which defaults to 20 px: Future Projects showed "$5,70(" for
    /// $5,700,000 and lost its Prov. column, Opportunities cut "2026-10-21" to "2026-10-2" (2026-10-22). About 300 BD
    /// columns had the same exposure. A column given a fixed width now gets that width as its floor, so the grid scrolls
    /// sideways rather than cut a value. Star columns, and any column whose XAML sets its own MinWidth, are left alone.
    /// Registered once at startup for every DataGrid, as <see cref="WindowFit"/> is for windows, so a new grid cannot
    /// reintroduce it.
    /// </summary>
    public static class GridColumnFloor
    {
        private static bool _registered;
        private static readonly ConditionalWeakTable<DataGrid, object> Done = new();

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            // SizeChanged, not Loaded: every grid raises it on its first layout (see WindowFit for why Loaded is not used).
            EventManager.RegisterClassHandler(typeof(DataGrid), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler((s, _) =>
            {
                if (s is not DataGrid g || Done.TryGetValue(g, out var seen)) return;
                Done.Add(g, new object());
                Apply(g);
            }));
        }

        /// <summary>Sets MinWidth = Width on each pixel-width column that does not set MinWidth itself.</summary>
        public static void Apply(DataGrid grid)
        {
            foreach (var column in grid.Columns)
            {
                if (!column.Width.IsAbsolute) continue;
                if (column.ReadLocalValue(DataGridColumn.MinWidthProperty) != DependencyProperty.UnsetValue) continue;
                column.MinWidth = column.Width.Value;
            }
        }
    }
}
