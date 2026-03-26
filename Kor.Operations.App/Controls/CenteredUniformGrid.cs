#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Kor.Operations.Controls
{
    /// <summary>
    /// UniformGrid variant that:
    /// - ignores Collapsed children when computing rows/columns
    /// - centers the final row when it is not full
    /// </summary>
    public sealed class CenteredUniformGrid : UniformGrid
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var visible = GetVisibleChildren();
            int count = visible.Count;
            if (count == 0)
            {
                // Still measure collapsed children to keep WPF happy.
                foreach (UIElement child in InternalChildren)
                    child?.Measure(new Size(0, 0));
                return new Size(0, 0);
            }

            int columns = Columns;
            int rows = Rows;

            if (rows == 0 && columns == 0)
            {
                columns = (int)Math.Ceiling(Math.Sqrt(count));
                rows = (int)Math.Ceiling((double)count / columns);
            }
            else if (rows == 0)
            {
                columns = Math.Max(1, columns);
                rows = (int)Math.Ceiling((double)count / columns);
            }
            else if (columns == 0)
            {
                rows = Math.Max(1, rows);
                columns = (int)Math.Ceiling((double)count / rows);
            }

            columns = Math.Max(1, columns);
            rows = Math.Max(1, rows);

            var childConstraint = new Size(availableSize.Width / columns, availableSize.Height / rows);

            double maxW = 0;
            double maxH = 0;

            foreach (UIElement child in InternalChildren)
            {
                if (child == null) continue;
                if (child.Visibility == Visibility.Collapsed)
                {
                    child.Measure(new Size(0, 0));
                    continue;
                }

                child.Measure(childConstraint);
                var d = child.DesiredSize;
                if (d.Width > maxW) maxW = d.Width;
                if (d.Height > maxH) maxH = d.Height;
            }

            return new Size(maxW * columns, maxH * rows);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var visible = GetVisibleChildren();
            int count = visible.Count;
            if (count == 0) return finalSize;

            int columns = Columns;
            int rows = Rows;

            if (rows == 0 && columns == 0)
            {
                columns = (int)Math.Ceiling(Math.Sqrt(count));
                rows = (int)Math.Ceiling((double)count / columns);
            }
            else if (rows == 0)
            {
                columns = Math.Max(1, columns);
                rows = (int)Math.Ceiling((double)count / columns);
            }
            else if (columns == 0)
            {
                rows = Math.Max(1, rows);
                columns = (int)Math.Ceiling((double)count / rows);
            }

            columns = Math.Max(1, columns);
            rows = Math.Max(1, rows);

            double cellW = finalSize.Width / columns;
            double cellH = finalSize.Height / rows;

            // Arrange collapsed children to an empty rect so they don't keep stale layout state.
            foreach (UIElement child in InternalChildren)
            {
                if (child == null) continue;
                if (child.Visibility == Visibility.Collapsed)
                    child.Arrange(new Rect(0, 0, 0, 0));
            }

            int lastRow = rows - 1;
            int itemsInLastRow = count - (lastRow * columns);
            itemsInLastRow = Math.Max(0, Math.Min(columns, itemsInLastRow));
            double lastRowOffset = (itemsInLastRow > 0 && itemsInLastRow < columns)
                ? ((columns - itemsInLastRow) * cellW / 2.0)
                : 0.0;

            for (int i = 0; i < count; i++)
            {
                var child = visible[i];
                int row = i / columns;
                int col = i % columns;

                double x = col * cellW;
                if (row == lastRow)
                    x += lastRowOffset;

                double y = row * cellH;
                child.Arrange(new Rect(x, y, cellW, cellH));
            }

            return finalSize;
        }

        private List<UIElement> GetVisibleChildren()
        {
            var list = new List<UIElement>();
            foreach (UIElement child in InternalChildren)
            {
                if (child == null) continue;
                if (child.Visibility == Visibility.Collapsed) continue;
                list.Add(child);
            }
            return list;
        }
    }
}
