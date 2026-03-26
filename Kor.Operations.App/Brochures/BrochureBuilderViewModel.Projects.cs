#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Brochures
{
    public sealed partial class BrochureBuilderViewModel
    {
        public ICommand AddProjectCommand { get; private set; } = null!;
        public ICommand EditProjectCommand { get; private set; } = null!;
        public ICommand SaveEditCommand { get; private set; } = null!;
        public ICommand CancelEditCommand { get; private set; } = null!;
        public ICommand MoveProjectUpCommand { get; private set; } = null!;
        public ICommand MoveProjectDownCommand { get; private set; } = null!;
        public ICommand InsertProjectPageBreakCommand { get; private set; } = null!;
        public ICommand RemoveProjectFromSectionCommand { get; private set; } = null!;
        public ICommand RemovePhotoCommand { get; private set; } = null!;

        [MemberNotNull(
            nameof(AddProjectCommand), nameof(EditProjectCommand), nameof(SaveEditCommand),
            nameof(CancelEditCommand), nameof(MoveProjectUpCommand), nameof(MoveProjectDownCommand), nameof(InsertProjectPageBreakCommand),
            nameof(RemoveProjectFromSectionCommand), nameof(RemovePhotoCommand))]
        private void InitProjectCommands()
        {
            AddProjectCommand = new RelayCommand(ExecAddProject);
            EditProjectCommand = new RelayCommand(ExecEditProject);
            SaveEditCommand = new RelayCommand(ExecSaveEdit);
            CancelEditCommand = new RelayCommand(ExecCancelEdit);
            MoveProjectUpCommand = new RelayCommand(ExecMoveProjectUp,
                p => p is BrochureProject proj && SelectedBlock?.Section is { } s && s.Projects.IndexOf(proj) > 0);
            MoveProjectDownCommand = new RelayCommand(ExecMoveProjectDown,
                p => p is BrochureProject proj && SelectedBlock?.Section is { } s && s.Projects.IndexOf(proj) < s.Projects.Count - 1);
            InsertProjectPageBreakCommand = new RelayCommand(ExecInsertProjectPageBreak);
            RemoveProjectFromSectionCommand = new RelayCommand(ExecRemoveProjectFromSection);
            RemovePhotoCommand = new RelayCommand(ExecRemovePhoto);
        }

        private static void SwapPageBreakAtIndices(List<int> breaks, int a, int b)
        {
            var hasA = breaks.Contains(a);
            var hasB = breaks.Contains(b);
            if (hasA == hasB) return;
            if (hasA) { breaks.Remove(a); breaks.Add(b); }
            else      { breaks.Remove(b); breaks.Add(a); }
            breaks.Sort();
        }

        private void ExecAddProject(object? _)
        {
            if (IsEditingProject)
            {
                MessageBox.Show(
                    "Finish editing the current project first - click Save Changes or Cancel Edit.",
                    "Edit In Progress",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (SelectedSection is null || _selectedSectionBlock is null)
            {
                MessageBox.Show(
                    "Select or create a section before adding a project",
                    "Missing Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!Project.ValidateForm()) return;

            SelectedSection.Projects.Add(new BrochureProject
            {
                SectionLabel = SelectedSection.Heading,
                ProjectName = Project.ProjectName,
                ProjectDescription = Project.ProjectDescription,
                Client = Project.Client,
                Architect = Project.Architect,
                Photos = Project.Photos.ToList()
            });

            RefreshBlock(_selectedSectionBlock);
            ClearProjectForm();
        }

        private void ExecEditProject(object? parameter)
        {
            if (parameter is not BrochureProject project) return;

            var block = FindSectionBlockContaining(project);
            if (block?.Section is not null)
                SelectedSection = block.Section;

            Project.ProjectName = project.ProjectName;
            Project.ProjectDescription = project.ProjectDescription;
            Project.Client = project.Client;
            Project.Architect = project.Architect;
            Project.Photos = new ObservableCollection<BrochurePhoto>(project.Photos);

            block = Blocks
                .Where(static b => b.BlockType == BrochureBlockType.Section)
                .FirstOrDefault(b => b.Section is not null && b.Section.Projects.Contains(project));
            if (block?.Section is not null)
                SelectedProjectIndex = block.Section.Projects.IndexOf(project);

            _editingBlock = block;
            _editingProject = project;
            IsEditingProject = true;
        }

        private void ExecSaveEdit(object? _)
        {
            if (_editingProject is null) return;
            if (!Project.ValidateForm()) return;

            var block = _editingBlock;
            if (block?.Section is null) return;

            _editingProject.ProjectName = Project.ProjectName;
            _editingProject.SectionLabel = block.Section.Heading;
            _editingProject.ProjectDescription = Project.ProjectDescription;
            _editingProject.Client = Project.Client;
            _editingProject.Architect = Project.Architect;
            _editingProject.Photos = Project.Photos.ToList();

            RefreshBlock(block);
            ClearProjectForm();
            IsEditingProject = false;
            _editingProject = null;
        }

        private void ExecCancelEdit(object? _)
        {
            ClearProjectForm();
            IsEditingProject = false;
            _editingBlock = null;
            _editingProject = null;
        }

        private void ExecMoveProjectUp(object? parameter)
        {
            if (parameter is not BrochureProject project) return;
            if (SelectedBlock?.Section is not { } section) return;
            var index = section.Projects.IndexOf(project);
            if (index <= 0) return;
            SwapPageBreakAtIndices(section.PageBreakAfterProjectIndex, index - 1, index);
            section.Projects.Move(index, index - 1);
            RefreshBlock(SelectedBlock);
        }

        private void ExecMoveProjectDown(object? parameter)
        {
            if (parameter is not BrochureProject project) return;
            if (SelectedBlock?.Section is not { } section) return;
            var index = section.Projects.IndexOf(project);
            if (index < 0 || index >= section.Projects.Count - 1) return;
            SwapPageBreakAtIndices(section.PageBreakAfterProjectIndex, index, index + 1);
            section.Projects.Move(index, index + 1);
            RefreshBlock(SelectedBlock);
        }

        private void ExecInsertProjectPageBreak(object? parameter)
        {
            if (parameter is not int projectIndex) return;
            if (SelectedBlock?.Section is not { } section) return;

            if (section.PageBreakAfterProjectIndex.Contains(projectIndex))
                section.PageBreakAfterProjectIndex.Remove(projectIndex);
            else
                section.PageBreakAfterProjectIndex.Add(projectIndex);

            section.PageBreakAfterProjectIndex.Sort();
            NotifyProjectPageBreakStateChanged();
        }

        private void ExecRemoveProjectFromSection(object? parameter)
        {
            if (parameter is not BrochureProject project) return;
            var block = FindSectionBlockContaining(project);
            if (block?.Section is null) return;
            block.Section.Projects.Remove(project);
            RefreshBlock(block);
        }

        private void ExecRemovePhoto(object? parameter)
        {
            if (parameter is not BrochurePhoto photo) return;
            Project.Photos.Remove(photo);
        }
    }
}

