#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using Kor.Operations.App.Email;
using MessageBox = System.Windows.MessageBox;

namespace Kor.Operations
{
    public partial class AttachmentPickerDialog : Window
    {
        private readonly EmailAttachmentService _attachmentService;
        private readonly FolderPickerService _folderPickerService;
        private readonly string _emailPath;

        public ObservableCollection<AttachmentRow> Items { get; } = new();

        public string? DestinationFolder { get; private set; }
        public IReadOnlySet<int> SelectedIndices { get; private set; } = new HashSet<int>();

        internal AttachmentPickerDialog(
            EmailAttachmentService attachmentService,
            FolderPickerService folderPickerService,
            string emailPath,
            string emailDisplaySubject,
            string initialFolder)
        {
            _attachmentService = attachmentService ?? throw new ArgumentNullException(nameof(attachmentService));
            _folderPickerService = folderPickerService ?? throw new ArgumentNullException(nameof(folderPickerService));
            _emailPath = emailPath ?? throw new ArgumentNullException(nameof(emailPath));

            InitializeComponent();

            EmailSubjectText.Text = string.IsNullOrWhiteSpace(emailDisplaySubject)
                ? "(no subject)"
                : emailDisplaySubject;
            EmailFileNameText.Text = Path.GetFileName(emailPath);
            FolderBox.Text = initialFolder ?? string.Empty;

            AttachmentsList.ItemsSource = Items;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var infos = _attachmentService.ListAttachments(_emailPath);
            int hiddenInline = 0;

            foreach (var info in infos)
            {
                // Skip unsavable attachments entirely (blocked extension / too large / empty).
                // Existing SaveAttachmentsAsync would skip these silently anyway, so showing
                // them here would only confuse the user.
                if (info.SkipReason != null)
                    continue;

                var row = new AttachmentRow
                {
                    Index = info.Index,
                    FileName = info.FileName,
                    SizeBytes = info.SizeBytes,
                    IsLikelyInlineImage = info.IsLikelyInlineImage,
                    IsSelected = !info.IsLikelyInlineImage
                };

                if (info.IsLikelyInlineImage)
                    hiddenInline++;

                Items.Add(row);
            }

            if (Items.Count == 0)
            {
                HintText.Text = "No saveable attachments found.";
                SaveBtn.IsEnabled = false;
            }
            else if (hiddenInline > 0)
            {
                HintText.Text = $"{hiddenInline} likely inline image(s) auto-unchecked.";
            }
        }

        private void SelectAllBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in Items)
                row.IsSelected = true;
        }

        private void SelectNoneBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in Items)
                row.IsSelected = false;
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            string seed = !string.IsNullOrWhiteSpace(FolderBox.Text) && Directory.Exists(FolderBox.Text)
                ? FolderBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            string? picked = _folderPickerService.PickFolder("Choose folder to save attachments", seed);
            if (!string.IsNullOrWhiteSpace(picked))
                FolderBox.Text = picked;
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            string folder = FolderBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(folder))
            {
                MessageBox.Show(this,
                    "Choose a destination folder before saving.",
                    "Folder required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var selected = Items.Where(r => r.IsSelected).Select(r => r.Index).ToHashSet();
            if (selected.Count == 0)
            {
                MessageBox.Show(this,
                    "Select at least one attachment, or click Skip Email to skip.",
                    "Nothing selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DestinationFolder = folder;
            SelectedIndices = selected;
            DialogResult = true;
            Close();
        }

        private void SkipBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public sealed class AttachmentRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public int Index { get; init; }
        public string FileName { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public bool IsLikelyInlineImage { get; init; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SizeDisplay
        {
            get
            {
                if (SizeBytes < 1024) return $"{SizeBytes} B";
                if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F1} KB";
                return $"{SizeBytes / (1024.0 * 1024.0):F2} MB";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName!));
        }
    }
}
