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
        }
    }
}

