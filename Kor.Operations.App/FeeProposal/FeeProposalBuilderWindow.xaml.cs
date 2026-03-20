#nullable enable
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using Kor.Operations.App.FeeProposal.Editors;

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
            BlockEditorHost.Content = BuildEmptyEditor();
        }

        private void BlockList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm.SelectedBlock is not { } selected)
            {
                BlockEditorHost.Content = BuildEmptyEditor();
                return;
            }

            BlockEditorHost.Content = BuildEditor(selected);
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

        private UIElement BuildEmptyEditor()
        {
            return new Border
            {
                BorderBrush = (System.Windows.Media.Brush)FindResource("Panel.Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#FFF8FAFC")!,
                Padding = new Thickness(18),
                Child = new TextBlock
                {
                    Text = "Select a block to edit",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (System.Windows.Media.Brush)FindResource("Text.Secondary"),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                },
            };
        }

        private UIElement? BuildEditor(FeeProposalBlockViewModel vm) => vm.Block.BlockType switch
        {
            ProposalBlockType.Cover => new CoverEditor { DataContext = vm.Block.Cover, Tag = _vm.StaffMembers },
            ProposalBlockType.Introduction => new IntroductionEditor { DataContext = vm.Block.Introduction, Tag = _vm.StaffMembers },
            ProposalBlockType.Company => new CompanyEditor { DataContext = vm.Block.Company },
            ProposalBlockType.Personnel => new PersonnelEditor { DataContext = vm.Block.Personnel, Tag = _vm.StaffMembers },
            ProposalBlockType.References => new ReferencesEditor { DataContext = vm.Block.References },
            ProposalBlockType.ProjectDescription => new ProjectDescriptionEditor { DataContext = vm.Block.ProjectDescription },
            ProposalBlockType.FeeTable => new FeeTableEditor { DataContext = vm.Block.FeeTable },
            ProposalBlockType.Scope => new ScopeEditor { DataContext = vm.Block.Scope },
            ProposalBlockType.ExcludedServices => new ExcludedServicesEditor { DataContext = vm.Block.ExcludedServices },
            ProposalBlockType.ApprovalToProceed => new ApprovalToProceedEditor { DataContext = vm.Block.ApprovalToProceed },
            ProposalBlockType.SignaturePage => new SignaturePageEditor { DataContext = vm.Block.SignaturePage, Tag = _vm.StaffMembers },
            ProposalBlockType.RatesTable => new RatesTableEditor { DataContext = vm.Block.RatesTable },
            ProposalBlockType.FreeText => new FreeTextEditor { DataContext = vm.Block.FreeText },
            _ => BuildEmptyEditor(),
        };
    }
}
