#nullable enable
using System;
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
        public ICommand MoveOverviewSectionCommand { get; private set; } = null!;
        public ICommand InsertOverviewPageBreakCommand { get; private set; } = null!;

        private void InitOverviewCommands()
        {
            AddOverviewSectionCommand = new RelayCommand(ExecAddOverviewSection);
            BeginEditOverviewCommand = new RelayCommand(ExecBeginEditOverview);
            SaveOverviewEditCommand = new RelayCommand(ExecSaveOverviewEdit);
            CancelOverviewEditCommand = new RelayCommand(ExecCancelOverviewEdit);
            RemoveOverviewSectionCommand = new RelayCommand(ExecRemoveOverviewSection);
            MoveOverviewSectionCommand = new RelayCommand(ExecMoveOverviewSection);
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

        private void ExecMoveOverviewSection(object? parameter)
        {
            if (parameter is not Tuple<int, int> moveRequest) return;
            if (SelectedBlock is not { } block) return;

            var (fromIndex, toIndex) = moveRequest;
            if (fromIndex < 0 || fromIndex >= block.OverviewSections.Count ||
                toIndex < 0 || toIndex >= block.OverviewSections.Count) return;

            var hadBreak = block.PageBreakAfterOverviewIndex.Contains(fromIndex);
            block.PageBreakAfterOverviewIndex.Remove(fromIndex);

            for (var i = 0; i < block.PageBreakAfterOverviewIndex.Count; i++)
            {
                var bi = block.PageBreakAfterOverviewIndex[i];
                if (bi > fromIndex) bi--;
                if (bi >= toIndex) bi++;
                block.PageBreakAfterOverviewIndex[i] = bi;
            }

            if (hadBreak)
                block.PageBreakAfterOverviewIndex.Add(toIndex - 1);

            block.PageBreakAfterOverviewIndex.Sort();

            var section = block.OverviewSections[fromIndex];
            block.OverviewSections.RemoveAt(fromIndex);
            block.OverviewSections.Insert(toIndex, section);

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

