#nullable enable
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;

namespace Kor.Operations.App.FeeProposal
{
    public partial class FeeProposalBuilderWindow : Window
    {
        private readonly FeeProposalBuilderViewModel _vm;

        public FeeProposalBuilderWindow(
            FeeProposalStore proposalStore,
            ProposalBlockLibraryStore libraryStore,
            ProposalStaffStore staffStore)
        {
            InitializeComponent();
            _vm = new FeeProposalBuilderViewModel(proposalStore, libraryStore, staffStore);
            DataContext = _vm;
        }

        private void NewProposal_Click(object sender, RoutedEventArgs e) => _vm.NewProposal();

        private void OpenProposal_Click(object sender, RoutedEventArgs e)
        {
            var prompt = _vm.BuildOpenPromptText();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                MessageBox.Show(this, "No saved proposals were found.", "Open Proposal", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selection = Interaction.InputBox(
                $"Enter a proposal name or id:{System.Environment.NewLine}{System.Environment.NewLine}{prompt}",
                "Open Proposal",
                string.Empty);
            if (string.IsNullOrWhiteSpace(selection))
                return;

            var proposal = _vm.FindProposalByIdOrName(selection);
            if (proposal is null)
            {
                MessageBox.Show(this, "Proposal not found.", "Open Proposal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _vm.OpenProposal(proposal);
        }

        private void SaveProposal_Click(object sender, RoutedEventArgs e)
        {
            _vm.SaveProposal();
            MessageBox.Show(this, "Proposal saved.", "Save Proposal", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void InsertTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ProposalBlockTemplate template })
                _vm.InsertFromTemplate(template);
        }

        private void AddBlankBlock_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_vm.SelectedBlockTypeName))
                return;

            if (System.Enum.TryParse<ProposalBlockType>(_vm.SelectedBlockTypeName, out var type))
                _vm.InsertBlankBlock(type);
        }

        private void MoveBlockUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: FeeProposalBlockViewModel vm })
                _vm.MoveUp(vm);
        }

        private void MoveBlockDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: FeeProposalBlockViewModel vm })
                _vm.MoveDown(vm);
        }

        private void DeleteBlock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: FeeProposalBlockViewModel vm })
                _vm.DeleteBlock(vm);
        }

        private void SaveBlockAsTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: FeeProposalBlockViewModel vm })
                return;

            var name = Interaction.InputBox("Template name:", "Save as Template", vm.TemplateName);
            if (string.IsNullOrWhiteSpace(name))
                return;

            _vm.SaveAsTemplate(vm, name);
        }
    }
}
