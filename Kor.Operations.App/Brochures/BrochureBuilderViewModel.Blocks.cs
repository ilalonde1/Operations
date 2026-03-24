#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Brochures
{
    public sealed partial class BrochureBuilderViewModel
    {
        public ICommand AddSectionCommand { get; private set; } = null!;
        public ICommand AddPersonnelBlockCommand { get; private set; } = null!;
        public ICommand AddCompanyOverviewCommand { get; private set; } = null!;
        public ICommand AddPageBreakCommand { get; private set; } = null!;
        public ICommand AddContactPageCommand { get; private set; } = null!;
        public ICommand AddClientListBlockCommand { get; private set; } = null!;
        public ICommand RemoveBlockCommand { get; private set; } = null!;
        public ICommand MoveBlockUpCommand { get; private set; } = null!;
        public ICommand MoveBlockDownCommand { get; private set; } = null!;

        [MemberNotNull(
            nameof(AddSectionCommand), nameof(AddPersonnelBlockCommand), nameof(AddCompanyOverviewCommand),
            nameof(AddPageBreakCommand), nameof(AddContactPageCommand), nameof(AddClientListBlockCommand), nameof(RemoveBlockCommand),
            nameof(MoveBlockUpCommand), nameof(MoveBlockDownCommand))]
        private void InitBlockCommands()
        {
            AddSectionCommand = new RelayCommand(ExecAddSection);
            AddPersonnelBlockCommand = new RelayCommand(ExecAddPersonnelBlock);
            AddCompanyOverviewCommand = new RelayCommand(ExecAddCompanyOverview);
            AddPageBreakCommand = new RelayCommand(ExecAddPageBreak);
            AddContactPageCommand = new RelayCommand(ExecAddContactPage);
            AddClientListBlockCommand = new RelayCommand(ExecAddClientListBlock);
            RemoveBlockCommand = new RelayCommand(ExecRemoveBlock);
            MoveBlockUpCommand = new RelayCommand(ExecMoveBlockUp,
                p => p is BrochureBlock b && Blocks.IndexOf(b) > 0);
            MoveBlockDownCommand = new RelayCommand(ExecMoveBlockDown,
                p => p is BrochureBlock b && Blocks.IndexOf(b) < Blocks.Count - 1);
        }

        private void ExecAddSection(object? _)
        {
            if (string.IsNullOrWhiteSpace(Overview.SectionHeading))
            {
                MessageBox.Show(
                    "Section Heading is required.",
                    "Missing Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var block = new BrochureBlock
            {
                BlockType = BrochureBlockType.Section,
                Section = new BrochureSection
                {
                    Heading = Overview.SectionHeading,
                    Blurb = Overview.SectionBlurb
                }
            };

            Blocks.Add(block);
            Overview.ClearSectionForm();
            SelectedSection = block.Section;
        }

        private void ExecAddPersonnelBlock(object? _)
        {
            Blocks.Add(new BrochureBlock
            {
                BlockType = BrochureBlockType.Personnel,
                People = new System.Collections.ObjectModel.ObservableCollection<BrochurePerson>()
            });
            Person.ClearForm();
        }

        private void ExecAddCompanyOverview(object? _)
        {
            if (HasCompanyOverview)
            {
                MessageBox.Show(
                    "A company overview block already exists",
                    "Duplicate Block",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Blocks.Insert(0, new BrochureBlock
            {
                BlockType = BrochureBlockType.CompanyOverview,
                OverviewSections = new System.Collections.ObjectModel.ObservableCollection<BrochureOverviewSection>()
            });
            OnPropertyChanged(nameof(HasCompanyOverview));
            OnPropertyChanged(nameof(SectionsList));
        }

        private void ExecAddPageBreak(object? _)
        {
            var insertAt = SelectedBlockIndex >= 0 ? SelectedBlockIndex + 1 : Blocks.Count;
            Blocks.Insert(insertAt, new BrochureBlock { BlockType = BrochureBlockType.PageBreak });
        }

        private void ExecAddContactPage(object? _)
        {
            if (HasContactPage)
            {
                MessageBox.Show(
                    "A contact page already exists",
                    "Duplicate Block",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Blocks.Add(new BrochureBlock { BlockType = BrochureBlockType.Contact });
            OnPropertyChanged(nameof(HasContactPage));
        }

        private void ExecAddClientListBlock(object? _)
        {
            Blocks.Add(new BrochureBlock { BlockType = BrochureBlockType.ClientList });
        }

        private void ExecRemoveBlock(object? parameter)
        {
            if (parameter is not BrochureBlock block) return;

            var hasContent = block.BlockType switch
            {
                BrochureBlockType.Section => block.Section?.Projects.Count > 0,
                BrochureBlockType.Personnel => block.People.Count > 0,
                BrochureBlockType.CompanyOverview => block.OverviewSections.Count > 0,
                BrochureBlockType.ClientList => block.ClientNames.Count > 0,
                _ => false
            };

            if (hasContent)
            {
                var confirm = MessageBox.Show(
                    "This block contains content. Remove it?",
                    "Remove Block",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;
            }

            Blocks.Remove(block);

            if (block.BlockType == BrochureBlockType.Section && ReferenceEquals(SelectedSection, block.Section))
            {
                SelectedSection = SectionsList.FirstOrDefault();
                SelectedProjectIndex = -1;
            }
        }

        private void ExecMoveBlockUp(object? parameter)
        {
            if (parameter is not BrochureBlock block) return;
            var index = Blocks.IndexOf(block);
            if (index <= 0) return;
            Blocks.Move(index, index - 1);
            OnPropertyChanged(nameof(SectionsList));
            OnPropertyChanged(nameof(HasCompanyOverview));
            OnPropertyChanged(nameof(HasContactPage));
            SetDirty();
        }

        private void ExecMoveBlockDown(object? parameter)
        {
            if (parameter is not BrochureBlock block) return;
            var index = Blocks.IndexOf(block);
            if (index < 0 || index >= Blocks.Count - 1) return;
            Blocks.Move(index, index + 1);
            OnPropertyChanged(nameof(SectionsList));
            OnPropertyChanged(nameof(HasCompanyOverview));
            OnPropertyChanged(nameof(HasContactPage));
            SetDirty();
        }
    }
}

