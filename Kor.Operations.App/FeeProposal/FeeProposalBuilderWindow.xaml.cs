#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.FeeProposal.Editors;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using Microsoft.Extensions.DependencyInjection;

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
            ProposalLibrarySeed.EnsureSeeded(libraryStore);
            _vm.RefreshLibrary();
            BlockEditorHost.Content = BuildEmptyEditor();
            RefreshCoverEditor();
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

        private void NewProposal_Click(object sender, RoutedEventArgs e)
        {
            _vm.NewProposal();
            RefreshCoverEditor();
        }

        private void OpenProposal_Click(object sender, RoutedEventArgs e)
        {
            var store = ((global::Kor.Operations.OperationsApp)Application.Current).Services
                .GetRequiredService<Kor.Operations.Core.Services.FeeProposalStore>();
            var dlg = new OpenProposalDialog(store) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.SelectedProposal is { } proposal)
            {
                _vm.OpenProposal(proposal);
                RefreshCoverEditor();
            }
        }

        private void SaveProposal_Click(object sender, RoutedEventArgs e)
        {
            _vm.SaveProposal();
            MessageBox.Show(this, "Proposal saved.", "Save Proposal", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ManageStaff_Click(object sender, RoutedEventArgs e)
        {
            var store = ((global::Kor.Operations.OperationsApp)Application.Current).Services
                .GetRequiredService<Kor.Operations.Core.Services.ProposalStaffStore>();
            var win = new StaffManagement.ProposalStaffWindow(store) { Owner = this };
            win.ShowDialog();
            _vm.ReloadStaff();
        }

        private async void GeneratePdf_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save PDF",
                Filter = "PDF files|*.pdf",
                FileName = $"{_vm.DocumentName}.pdf",
            };
            if (dlg.ShowDialog() != true)
                return;

            var staff = _vm.StaffMembers.ToList();
            _vm.SaveProposal();

            try
            {
                var renderer = ((global::Kor.Operations.OperationsApp)Application.Current).Services
                    .GetRequiredService<Kor.Operations.Rendering.Proposal.IFeeProposalRenderer>();
                await renderer.RenderAsync(_vm.CurrentProposal, staff, dlg.FileName, CancellationToken.None);
                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PDF generation failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void GenerateDocx_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Word Document",
                Filter = "Word documents|*.docx",
                FileName = $"{_vm.DocumentName}.docx",
            };
            if (dlg.ShowDialog() != true)
                return;

            var staff = _vm.StaffMembers.ToList();
            _vm.SaveProposal();

            try
            {
                var renderer = ((global::Kor.Operations.OperationsApp)Application.Current).Services
                    .GetRequiredService<Kor.Operations.Rendering.Proposal.IFeeProposalDocxRenderer>();
                await renderer.RenderAsync(_vm.CurrentProposal, staff, dlg.FileName, CancellationToken.None);
                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"DOCX generation failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            if ((sender as Button)?.DataContext is not FeeProposalBlockViewModel vm)
                return;

            var result = MessageBox.Show(
                $"Remove \"{vm.TemplateName}\" block?",
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                _vm.DeleteBlock(vm);
        }

        private void SaveBlockAsTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: FeeProposalBlockViewModel vm })
                return;

            if (_vm.SaveAsTemplate(vm))
            {
                MessageBox.Show(
                    this,
                    "Saved to library.",
                    "Template Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void RefreshCoverEditor()
        {
            CoverEditorHost.Content = new CoverEditor
            {
                DataContext = _vm.CoverBlockVm.Block.Cover,
                StaffMembers = _vm.StaffMembers
            };
        }

        private void GoNext_Click(object sender, RoutedEventArgs e) => _vm.GoNext();

        private void GoPrev_Click(object sender, RoutedEventArgs e) => _vm.GoPrev();

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
            ProposalBlockType.Cover => new CoverEditor { DataContext = vm.Block.Cover, StaffMembers = _vm.StaffMembers },
            ProposalBlockType.Introduction => new IntroductionEditor { DataContext = vm.Block.Introduction, StaffMembers = _vm.StaffMembers },
            ProposalBlockType.Company => new CompanyEditor { DataContext = vm.Block.Company },
            ProposalBlockType.Personnel => new PersonnelEditor { DataContext = vm.Block.Personnel, StaffMembers = _vm.StaffMembers },
            ProposalBlockType.References => new ReferencesEditor { DataContext = vm.Block.References },
            ProposalBlockType.ProjectDescription => new ProjectDescriptionEditor { DataContext = vm.Block.ProjectDescription },
            ProposalBlockType.FeeTable => new FeeTableEditor { DataContext = vm.Block.FeeTable },
            ProposalBlockType.Scope => new ScopeEditor { DataContext = vm.Block.Scope },
            ProposalBlockType.ExcludedServices => new ExcludedServicesEditor { DataContext = vm.Block.ExcludedServices },
            ProposalBlockType.ApprovalToProceed => new ApprovalToProceedEditor { DataContext = vm.Block.ApprovalToProceed },
            ProposalBlockType.SignaturePage => new SignaturePageEditor { DataContext = vm.Block.SignaturePage, StaffMembers = _vm.StaffMembers },
            ProposalBlockType.RatesTable => new RatesTableEditor { DataContext = vm.Block.RatesTable },
            ProposalBlockType.FreeText => new FreeTextEditor { DataContext = vm.Block.FreeText },
            ProposalBlockType.PageBreak => new System.Windows.Controls.TextBlock
            {
                Text = "Page Break  no settings",
                Foreground = System.Windows.Media.Brushes.Gray,
                FontStyle = System.Windows.FontStyles.Italic,
                Margin = new System.Windows.Thickness(16),
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
            },
            _ => BuildEmptyEditor(),
        };

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_vm.IsDirty)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Save before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                    _vm.SaveProposal();
                else if (result == MessageBoxResult.Cancel)
                    e.Cancel = true;
            }

            base.OnClosing(e);
        }
    }
}
