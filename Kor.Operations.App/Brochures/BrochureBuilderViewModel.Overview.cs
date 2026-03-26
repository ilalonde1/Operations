#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Brochures
{
    public sealed partial class BrochureBuilderViewModel
    {
        public ICommand AddOverviewSectionCommand { get; private set; } = null!;
        public ICommand BeginEditOverviewCommand { get; private set; } = null!;
        public ICommand SaveOverviewEditCommand { get; private set; } = null!;
        public ICommand CancelOverviewEditCommand { get; private set; } = null!;
        public ICommand RemoveOverviewSectionCommand { get; private set; } = null!;
        public ICommand MoveOverviewSectionUpCommand { get; private set; } = null!;
        public ICommand MoveOverviewSectionDownCommand { get; private set; } = null!;
        public ICommand InsertOverviewPageBreakCommand { get; private set; } = null!;

        [MemberNotNull(
            nameof(AddOverviewSectionCommand), nameof(BeginEditOverviewCommand), nameof(SaveOverviewEditCommand),
            nameof(CancelOverviewEditCommand), nameof(RemoveOverviewSectionCommand),
            nameof(MoveOverviewSectionUpCommand), nameof(MoveOverviewSectionDownCommand), nameof(InsertOverviewPageBreakCommand))]
        private void InitOverviewCommands()
        {
            AddOverviewSectionCommand = new RelayCommand(ExecAddOverviewSection);
            BeginEditOverviewCommand = new RelayCommand(ExecBeginEditOverview);
            SaveOverviewEditCommand = new RelayCommand(ExecSaveOverviewEdit);
            CancelOverviewEditCommand = new RelayCommand(ExecCancelOverviewEdit);
            RemoveOverviewSectionCommand = new RelayCommand(ExecRemoveOverviewSection);
            MoveOverviewSectionUpCommand = new RelayCommand(ExecMoveOverviewSectionUp,
                p => p is BrochureOverviewSection s && SelectedBlock is { } b && b.OverviewSections.IndexOf(s) > 0);
            MoveOverviewSectionDownCommand = new RelayCommand(ExecMoveOverviewSectionDown,
                p => p is BrochureOverviewSection s && SelectedBlock is { } b && b.OverviewSections.IndexOf(s) < b.OverviewSections.Count - 1);
            InsertOverviewPageBreakCommand = new RelayCommand(ExecInsertOverviewPageBreak);
        }

        private void ExecAddOverviewSection(object? parameter)
        {
            if (parameter is not BrochureBlock block || block.BlockType != BrochureBlockType.CompanyOverview) return;
            if (IsEditingOverview) return;

            if (string.IsNullOrWhiteSpace(Overview.OverviewHeading))
            {
                MessageBox.Show(
                    "Section Heading is required.",
                    "Missing Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            block.OverviewSections.Add(new BrochureOverviewSection
            {
                Heading = Overview.OverviewHeading,
                Body = Overview.OverviewBody
            });
            RefreshBlock(block);
            Overview.ClearOverviewForm();
        }

        private void ExecBeginEditOverview(object? parameter)
        {
            if (parameter is not int overviewIndex) return;
            if (SelectedBlock is not { BlockType: BrochureBlockType.CompanyOverview } block) return;
            if (overviewIndex < 0 || overviewIndex >= block.OverviewSections.Count) return;

            var section = block.OverviewSections[overviewIndex];
            SelectedOverviewIndex = overviewIndex;
            Overview.OverviewHeading = section.Heading;
            Overview.OverviewBody = section.Body;
            IsEditingOverview = true;
        }

        private void ExecSaveOverviewEdit(object? _)
        {
            if (!IsEditingOverview || SelectedOverviewIndex < 0) return;
            if (SelectedBlock is not { BlockType: BrochureBlockType.CompanyOverview } block) return;
            if (SelectedOverviewIndex >= block.OverviewSections.Count) return;

            if (string.IsNullOrWhiteSpace(Overview.OverviewHeading))
            {
                MessageBox.Show(
                    "Section Heading is required.",
                    "Missing Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var section = block.OverviewSections[SelectedOverviewIndex];
            section.Heading = Overview.OverviewHeading;
            section.Body = Overview.OverviewBody;

            SelectedOverviewIndex = -1;
            IsEditingOverview = false;
            Overview.ClearOverviewForm();
            NotifyOverviewEditStateChanged();
        }

        private void ExecCancelOverviewEdit(object? _)
        {
            SelectedOverviewIndex = -1;
            IsEditingOverview = false;
            Overview.ClearOverviewForm();
        }

        private void ExecRemoveOverviewSection(object? parameter)
        {
            if (parameter is not BrochureOverviewSection overviewSection) return;
            var block = Blocks.FirstOrDefault(c =>
                c.BlockType == BrochureBlockType.CompanyOverview &&
                c.OverviewSections.Contains(overviewSection));
            if (block is null) return;
            block.OverviewSections.Remove(overviewSection);
            RefreshBlock(block);
        }

        private void ExecMoveOverviewSectionUp(object? parameter)
        {
            if (parameter is not BrochureOverviewSection overviewSection) return;
            if (SelectedBlock is not { } block) return;
            var index = block.OverviewSections.IndexOf(overviewSection);
            if (index <= 0) return;
            SwapPageBreakAtIndices(block.PageBreakAfterOverviewIndex, index - 1, index);
            block.OverviewSections.Move(index, index - 1);
            RefreshBlock(block);
        }

        private void ExecMoveOverviewSectionDown(object? parameter)
        {
            if (parameter is not BrochureOverviewSection overviewSection) return;
            if (SelectedBlock is not { } block) return;
            var index = block.OverviewSections.IndexOf(overviewSection);
            if (index < 0 || index >= block.OverviewSections.Count - 1) return;
            SwapPageBreakAtIndices(block.PageBreakAfterOverviewIndex, index, index + 1);
            block.OverviewSections.Move(index, index + 1);
            RefreshBlock(block);
        }

        private void ExecInsertOverviewPageBreak(object? parameter)
        {
            if (parameter is not int overviewIndex) return;
            if (SelectedBlock is not { } block) return;

            if (block.PageBreakAfterOverviewIndex.Contains(overviewIndex))
                block.PageBreakAfterOverviewIndex.Remove(overviewIndex);
            else
                block.PageBreakAfterOverviewIndex.Add(overviewIndex);

            block.PageBreakAfterOverviewIndex.Sort();
            NotifyOverviewPageBreakStateChanged();
        }
    }
}

