#nullable enable
using System;
using System.Windows;
using Microsoft.Win32;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Services;

namespace Kor.Operations.GeneralTools
{
    public partial class BrochureBuilderWindow : Window
    {
        private readonly BrochureBuilderViewModel _viewModel;

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

        private void AddStat_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Stats.Add(new BrochureStat { Label = string.Empty, Value = string.Empty });
        }

        private void RemoveStat_Click(object sender, RoutedEventArgs e)
        {
            if (StatsGrid.SelectedItem is BrochureStat stat)
                _viewModel.Stats.Remove(stat);
        }
    }
}
