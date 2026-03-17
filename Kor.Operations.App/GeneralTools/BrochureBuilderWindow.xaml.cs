#nullable enable
using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using Microsoft.Win32;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Services;

namespace Kor.Operations.GeneralTools
{
    public partial class BrochureBuilderWindow : Window
    {
        private readonly BrochureBuilderViewModel _viewModel;
        private static readonly Brush DragHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5C36"));
        private static readonly Brush DefaultBlockBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E7E6E6"));
        private int _dragFromIndex = -1;
        private bool _isDragging;

        public BrochureBuilderWindow(BrochureBuilderViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = _viewModel;
            Loaded += BrochureBuilderWindow_Loaded;
        }

        private async void BrochureBuilderWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await HeaderLoader.ApplyAsync(HeaderBar);
        }

        private void AddPhoto_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Photos",
                Filter = "Image Files|*.jpg;*.jpeg;*.png|All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            foreach (var fileName in dialog.FileNames)
                _viewModel.Photos.Add(new BrochurePhoto { FilePath = fileName, Caption = string.Empty });
        }

        private void RemovePhoto_Click(object sender, RoutedEventArgs e)
        {
            if (PhotosList.SelectedItem is BrochurePhoto photo)
                _viewModel.Photos.Remove(photo);
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement handle)
                return;

            var contentPresenter = FindAncestor<ContentPresenter>(handle);
            if (contentPresenter is null)
                return;

            var block = contentPresenter.DataContext as BrochureBlock;
            if (block is null)
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
            if (_dragFromIndex < 0)
                return;

            var point = e.GetPosition(BlocksItemsControl);
            var targetIndex = GetBlockIndexFromPoint(point);
            if (targetIndex < 0 || _dragFromIndex == targetIndex)
            {
                _dragFromIndex = -1;
                return;
            }

            if (_viewModel.MoveBlockCommand.CanExecute(new Tuple<int, int>(_dragFromIndex, targetIndex)))
            {
                _viewModel.MoveBlockCommand.Execute(new Tuple<int, int>(_dragFromIndex, targetIndex));
            }

            _dragFromIndex = -1;
        }

        private static void BlockItem_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.BorderBrush = DragHighlightBrush;
            border.BorderThickness = new Thickness(1, 2, 1, 1);
        }

        private static void BlockItem_DragLeave(object sender, DragEventArgs e)
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

            return BlocksItemsControl.Items.Count > 0
                ? BlocksItemsControl.Items.Count - 1
                : -1;
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
