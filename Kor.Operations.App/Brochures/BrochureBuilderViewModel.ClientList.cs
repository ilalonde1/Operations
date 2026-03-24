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
        public ICommand AddClientNameCommand { get; private set; } = null!;
        public ICommand RemoveClientNameCommand { get; private set; } = null!;
        public ICommand MoveClientNameUpCommand { get; private set; } = null!;
        public ICommand MoveClientNameDownCommand { get; private set; } = null!;
        public ICommand PasteClientNamesCommand { get; private set; } = null!;

        private string _clientNameInput = string.Empty;
        public string ClientNameInput
        {
            get => _clientNameInput;
            set { _clientNameInput = value; OnPropertyChanged(); }
        }

        [MemberNotNull(
            nameof(AddClientNameCommand), nameof(RemoveClientNameCommand),
            nameof(MoveClientNameUpCommand), nameof(MoveClientNameDownCommand),
            nameof(PasteClientNamesCommand))]
        private void InitClientListCommands()
        {
            AddClientNameCommand = new RelayCommand(ExecAddClientName);
            RemoveClientNameCommand = new RelayCommand(ExecRemoveClientName);
            MoveClientNameUpCommand = new RelayCommand(ExecMoveClientNameUp);
            MoveClientNameDownCommand = new RelayCommand(ExecMoveClientNameDown);
            PasteClientNamesCommand = new RelayCommand(ExecPasteClientNames);
        }

        private void ExecAddClientName(object? _)
        {
            if (string.IsNullOrWhiteSpace(ClientNameInput)) return;
            if (SelectedBlock is not { BlockType: BrochureBlockType.ClientList } block) return;
            block.ClientNames.Add(ClientNameInput.Trim());
            ClientNameInput = string.Empty;
            SetDirty();
        }

        private void ExecRemoveClientName(object? parameter)
        {
            if (parameter is not int index) return;
            if (SelectedBlock is not { BlockType: BrochureBlockType.ClientList } block) return;
            if (index < 0 || index >= block.ClientNames.Count) return;
            block.ClientNames.RemoveAt(index);
            SetDirty();
        }

        private void ExecMoveClientNameUp(object? parameter)
        {
            if (parameter is not int index) return;
            if (SelectedBlock is not { BlockType: BrochureBlockType.ClientList } block) return;
            if (index <= 0) return;
            block.ClientNames.Move(index, index - 1);
            SetDirty();
        }

        private void ExecMoveClientNameDown(object? parameter)
        {
            if (parameter is not int index) return;
            if (SelectedBlock is not { BlockType: BrochureBlockType.ClientList } block) return;
            if (index < 0 || index >= block.ClientNames.Count - 1) return;
            block.ClientNames.Move(index, index + 1);
            SetDirty();
        }

        private void ExecPasteClientNames(object? _)
        {
            if (SelectedBlock is not { BlockType: BrochureBlockType.ClientList } block) return;
            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;
            var names = text
                .Split(new[] { '\n', '\r', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static n => n.Trim())
                .Where(static n => n.Length > 0);
            foreach (var name in names)
                block.ClientNames.Add(name);
            SetDirty();
        }
    }
}
