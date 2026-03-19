#nullable enable
using System;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Core.Models.Brochure;
using Microsoft.Win32;

namespace Kor.Operations.GeneralTools
{
    public sealed partial class BrochureBuilderViewModel
    {
        public ICommand AddPersonToBlockCommand { get; private set; } = null!;
        public ICommand BeginEditPersonCommand { get; private set; } = null!;
        public ICommand CancelPersonEditCommand { get; private set; } = null!;
        public ICommand RemovePersonCommand { get; private set; } = null!;
        public ICommand MovePersonCommand { get; private set; } = null!;
        public ICommand PickPersonPhotoCommand { get; private set; } = null!;

        private void InitPersonnelCommands()
        {
            AddPersonToBlockCommand = new RelayCommand(ExecAddPersonToBlock);
            BeginEditPersonCommand = new RelayCommand(ExecBeginEditPerson);
            CancelPersonEditCommand = new RelayCommand(ExecCancelPersonEdit);
            RemovePersonCommand = new RelayCommand(ExecRemovePerson);
            MovePersonCommand = new RelayCommand(ExecMovePerson);
            PickPersonPhotoCommand = new RelayCommand(ExecPickPersonPhoto);
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
                _editingPerson = null;
            }
            else
            {
                block.People.Add(new BrochurePerson
                {
                    Name = Person.PersonName,
                    Credentials = Person.PersonCredentials,
                    Bio = Person.PersonBio,
                    PhotoPath = Person.PersonPhotoPath
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

        private void ExecMovePerson(object? parameter)
        {
            if (parameter is not Tuple<int, int> moveRequest) return;
            if (SelectedBlock is not { } block) return;

            var (fromIndex, toIndex) = moveRequest;
            if (fromIndex < 0 || fromIndex >= block.People.Count ||
                toIndex < 0 || toIndex >= block.People.Count) return;

            var person = block.People[fromIndex];
            block.People.RemoveAt(fromIndex);
            block.People.Insert(toIndex, person);
            RefreshBlock(block);
        }

        private void ExecPickPersonPhoto(object? _)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
                Person.PersonPhotoPath = dialog.FileName;
        }
    }
}
