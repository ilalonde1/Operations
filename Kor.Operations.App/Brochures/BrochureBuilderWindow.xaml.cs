#nullable enable
using System;
using System.Windows;

namespace Kor.Operations.Brochures
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
            await _viewModel.InitializeAsync();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel.IsDirty)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Save before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                    _viewModel.SaveProposalCommand.Execute(null);
                else if (result == MessageBoxResult.Cancel)
                    e.Cancel = true;
            }
            base.OnClosing(e);
        }
    }
}

