#nullable enable
using System.Windows;
using Microsoft.Win32;
using Kor.Operations.Core.Services;

namespace Kor.Operations.App.FeeProposal.StaffManagement
{
    public partial class ProposalStaffWindow : Window
    {
        private readonly ProposalStaffViewModel _vm;

        public ProposalStaffWindow(IProposalStaffStore store)
        {
            InitializeComponent();
            _vm = new ProposalStaffViewModel(store);
            DataContext = _vm;
            Loaded += ProposalStaffWindow_Loaded;
        }

        private async void ProposalStaffWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await _vm.ReloadAsync();
        }

        private void Add_Click(object sender, RoutedEventArgs e) => _vm.AddNew();

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.Selected is null)
                return;

            var result = MessageBox.Show(
                $"Delete {_vm.Selected.FullName}?",
                "Fee Proposal — Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                _vm.DeleteSelected();
        }

        private async void Save_Click(object sender, RoutedEventArgs e) => await _vm.SaveAsync();

        private async void BrowseSignature_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.Selected is null)
                return;

            var dlg = new OpenFileDialog
            {
                Title = "Select Signature Image",
                Filter = "Image files|*.png;*.jpg;*.jpeg",
            };
            if (dlg.ShowDialog() == true)
            {
                _vm.Selected.SignatureImagePath = dlg.FileName;
                await _vm.SaveAsync();
            }
        }

        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_vm.HasChanges)
                await _vm.SaveAsync();
            base.OnClosing(e);
        }
    }
}
