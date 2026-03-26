#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.App;
using Kor.Operations.Core.Models.Brochure;
using Microsoft.Win32;

namespace Kor.Operations.Brochures
{
    public sealed partial class BrochureBuilderViewModel
    {
        public ICommand AddPersonToBlockCommand { get; private set; } = null!;
        public ICommand BeginEditPersonCommand { get; private set; } = null!;
        public ICommand CancelPersonEditCommand { get; private set; } = null!;
        public ICommand RemovePersonCommand { get; private set; } = null!;
        public ICommand MovePersonUpCommand { get; private set; } = null!;
        public ICommand MovePersonDownCommand { get; private set; } = null!;
        public ICommand PickPersonPhotoCommand { get; private set; } = null!;
        public ICommand FillPersonFromRosterCommand { get; private set; } = null!;

        [MemberNotNull(
            nameof(AddPersonToBlockCommand), nameof(BeginEditPersonCommand), nameof(CancelPersonEditCommand),
            nameof(RemovePersonCommand), nameof(MovePersonUpCommand), nameof(MovePersonDownCommand), nameof(PickPersonPhotoCommand),
            nameof(FillPersonFromRosterCommand))]
        private void InitPersonnelCommands()
        {
            AddPersonToBlockCommand = new RelayCommand(ExecAddPersonToBlock);
            BeginEditPersonCommand = new RelayCommand(ExecBeginEditPerson);
            CancelPersonEditCommand = new RelayCommand(ExecCancelPersonEdit);
            RemovePersonCommand = new RelayCommand(ExecRemovePerson);
            MovePersonUpCommand = new RelayCommand(ExecMovePersonUp,
                p => p is BrochurePerson person && FindPersonnelBlockContaining(person) is { } b && b.People.IndexOf(person) > 0);
            MovePersonDownCommand = new RelayCommand(ExecMovePersonDown,
                p => p is BrochurePerson person && FindPersonnelBlockContaining(person) is { } b && b.People.IndexOf(person) < b.People.Count - 1);
            PickPersonPhotoCommand = new AsyncRelayCommand(ExecPickPersonPhotoAsync);
            FillPersonFromRosterCommand = new RelayCommand(ExecFillPersonFromRoster);
        }

        private void ExecAddPersonToBlock(object? parameter)
        {
            if (parameter is not BrochureBlock block || block.BlockType != BrochureBlockType.Personnel) return;

            if (string.IsNullOrWhiteSpace(Person.PersonName))
            {
                MessageBox.Show(
                    "Name is required.",
                    "Missing Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_editingPerson is not null)
            {
                _editingPerson.Name = Person.PersonName;
                _editingPerson.Credentials = Person.PersonCredentials;
                _editingPerson.Bio = Person.PersonBio;
                _editingPerson.PhotoPath = Person.PersonPhotoPath;
                _editingPerson.PhotoBytes = Person.PersonPhotoBytes;
                _editingPerson = null;
            }
            else
            {
                block.People.Add(new BrochurePerson
                {
                    Name = Person.PersonName,
                    Credentials = Person.PersonCredentials,
                    Bio = Person.PersonBio,
                    PhotoPath = Person.PersonPhotoPath,
                    PhotoBytes = Person.PersonPhotoBytes
                });
            }

            RefreshBlock(block);
            Person.ClearForm();
            IsEditingPerson = false;
        }

        private void ExecBeginEditPerson(object? parameter)
        {
            if (parameter is not BrochurePerson person) return;
            Person.PersonName = person.Name;
            Person.PersonCredentials = person.Credentials;
            Person.PersonBio = person.Bio;
            Person.PersonPhotoPath = person.PhotoPath;
            Person.PersonPhotoBytes = person.PhotoBytes;
            _editingPerson = person;
            IsEditingPerson = true;
        }

        private void ExecCancelPersonEdit(object? _)
        {
            Person.ClearForm();
            _editingPerson = null;
            IsEditingPerson = false;
        }

        private void ExecRemovePerson(object? parameter)
        {
            if (parameter is not BrochurePerson person) return;
            var block = FindPersonnelBlockContaining(person);
            if (block is null) return;
            block.People.Remove(person);
            RefreshBlock(block);
        }

        private void ExecMovePersonUp(object? parameter)
        {
            if (parameter is not BrochurePerson person) return;
            var block = FindPersonnelBlockContaining(person);
            if (block is null) return;
            var index = block.People.IndexOf(person);
            if (index <= 0) return;
            block.People.Move(index, index - 1);
            RefreshBlock(block);
        }

        private void ExecMovePersonDown(object? parameter)
        {
            if (parameter is not BrochurePerson person) return;
            var block = FindPersonnelBlockContaining(person);
            if (block is null) return;
            var index = block.People.IndexOf(person);
            if (index < 0 || index >= block.People.Count - 1) return;
            block.People.Move(index, index + 1);
            RefreshBlock(block);
        }

        private async Task ExecPickPersonPhotoAsync(object? _)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Multiselect = false
            };
            if (dialog.ShowDialog(GetOwnerWindow()) == true)
            {
                Person.PersonPhotoPath = dialog.FileName;
                Person.PersonPhotoBytes = await File.ReadAllBytesAsync(dialog.FileName);
            }
        }

        private void ExecFillPersonFromRoster(object? _)
        {
            if (SelectedRosterMember is not { } staff) return;
            Person.PersonName = staff.FullName;
            Person.PersonCredentials = staff.Credentials;
            Person.PersonBio = staff.Bio;
            if (!string.IsNullOrWhiteSpace(staff.PhotoPath))
                Person.PersonPhotoPath = staff.PhotoPath;
            if (staff.PhotoBytes.Length > 0)
                Person.PersonPhotoBytes = staff.PhotoBytes;
        }
    }
}

