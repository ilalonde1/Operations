#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Brochures
{
    public partial class BrochureStructureStep : UserControl
    {
        private bool _sectionFormVisible;

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
            (DataContext as BrochureBuilderViewModel)?.Overview.ClearSectionForm();
        }

        private void AddSectionSubmit_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as BrochureBuilderViewModel;
            if (vm is null)
                return;

            var countBefore = vm.Blocks.Count;
            if (vm.AddSectionCommand.CanExecute(null))
                vm.AddSectionCommand.Execute(null);

            if (vm.Blocks.Count > countBefore)
            {
                _sectionFormVisible = false;
                InlineSectionForm.Visibility = Visibility.Collapsed;
            }
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
    }
}

