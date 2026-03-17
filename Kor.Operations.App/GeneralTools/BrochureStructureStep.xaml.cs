#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.GeneralTools
{
    public partial class BrochureStructureStep : UserControl
    {
        private static readonly Brush DragHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5C36"));
        private static readonly Brush DefaultBlockBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E7E6E6"));
        private bool _sectionFormVisible;
        private int _dragFromIndex = -1;
        private bool _isDragging;

        public BrochureStructureStep()
        {
            InitializeComponent();
        }

        private void AddSectionButton_Click(object sender, RoutedEventArgs e)
        {
            _sectionFormVisible = !_sectionFormVisible;
            InlineSectionForm.Visibility = _sectionFormVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CancelSectionForm_Click(object sender, RoutedEventArgs e)
        {
            _sectionFormVisible = false;
            InlineSectionForm.Visibility = Visibility.Collapsed;
        }

        private void EditBlockButton_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            if (sender is not FrameworkElement element || element.DataContext is not BrochureBlock block)
                return;

            var index = vm.Blocks.IndexOf(block);
            if (index < 0)
                return;

            vm.SelectedBlockIndex = index;
            vm.CurrentStep = 3;
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement handle)
                return;

            var contentPresenter = FindAncestor<ContentPresenter>(handle);
            if (contentPresenter?.DataContext is not BrochureBlock block)
                return;

            var index = BlocksItemsControl.Items.IndexOf(block);
            if (index < 0)
                return;

            _dragFromIndex = index;
            _isDragging = true;

            try
            {
                DragDrop.DoDragDrop(handle, index, DragDropEffects.Move);
            }
            finally
            {
                _isDragging = false;
            }
        }

        private void Blocks_Drop(object sender, DragEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null || _dragFromIndex < 0)
                return;

            var point = e.GetPosition(BlocksItemsControl);
            var targetIndex = GetBlockIndexFromPoint(point);
            if (targetIndex < 0 || _dragFromIndex == targetIndex)
            {
                _dragFromIndex = -1;
                return;
            }

            var moveRequest = new Tuple<int, int>(_dragFromIndex, targetIndex);
            if (vm.MoveBlockCommand.CanExecute(moveRequest))
                vm.MoveBlockCommand.Execute(moveRequest);

            _dragFromIndex = -1;
        }

        private void BlockItem_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.BorderBrush = DragHighlightBrush;
            border.BorderThickness = new Thickness(1, 2, 1, 1);
        }

        private void BlockItem_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.BorderBrush = DefaultBlockBrush;
            border.BorderThickness = new Thickness(1);
        }

        private int GetBlockIndexFromPoint(Point point)
        {
            for (var i = 0; i < BlocksItemsControl.Items.Count; i++)
            {
                if (BlocksItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                    continue;

                var bounds = new Rect(
                    container.TranslatePoint(new Point(0, 0), BlocksItemsControl),
                    new Size(container.ActualWidth, container.ActualHeight));

                if (bounds.Contains(point))
                    return i;
            }

            return BlocksItemsControl.Items.Count > 0 ? BlocksItemsControl.Items.Count - 1 : -1;
        }

        private static T? FindAncestor<T>(DependencyObject? current)
            where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T match)
                    return match;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
