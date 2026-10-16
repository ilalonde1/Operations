#nullable enable
using System.Windows;
using System;
using System.IO;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public sealed record SubmittedDetails(string FileName, string? FolderLink, string? SourcePath = null)
{
    internal static string? Validate(string? fileName, string? folderLink, string? sourcePath = null, long? existingFileId = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) && existingFileId is null) return "Browse to the submitted proposal PDF.";
        if (!string.IsNullOrWhiteSpace(sourcePath) && !string.Equals(Path.GetExtension(sourcePath), ".pdf", StringComparison.OrdinalIgnoreCase))
            return "Choose a PDF for the submitted proposal.";
        if (string.IsNullOrWhiteSpace(fileName)) return "Enter the submitted file name.";
        if (fileName.Length > 260) return "The file name must be 260 characters or fewer.";
        if (!string.Equals(Path.GetFileName(fileName.Trim()), fileName.Trim(), StringComparison.Ordinal)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "Enter a file name without a folder path.";
        if (!string.IsNullOrWhiteSpace(sourcePath) && !string.Equals(Path.GetExtension(fileName.Trim()), ".pdf", StringComparison.OrdinalIgnoreCase))
            return "Keep the .pdf extension on the submitted file name.";
        if (folderLink?.Length > 1000) return "The folder link must be 1000 characters or fewer.";
        return null;
    }
}

public partial class SubmittedDialog : Window
{
    private readonly long? _existingFileId;

    public SubmittedDialog(string? fileName = null, string? folderLink = null, long? existingFileId = null)
    {
        InitializeComponent();
        _existingFileId = existingFileId;
        FileNameBox.Text = fileName ?? "";
        FolderLinkBox.Text = folderLink ?? "";
        FileNameBox.IsReadOnly = existingFileId.HasValue;
        ExistingFileText.Text = existingFileId.HasValue ? $"Current attachment: {fileName}. Browse to replace it." : "";
    }

    public SubmittedDetails? Result { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Select the submitted proposal", Filter = "PDF files|*.pdf", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) SelectFile(dialog.FileName);
    }

    internal void SelectFile(string path)
    {
        SourcePathBox.Text = path;
        FileNameBox.Text = Path.GetFileName(path);
        FileNameBox.IsReadOnly = false;
        ErrorText.Text = "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var error = SubmittedDetails.Validate(FileNameBox.Text, FolderLinkBox.Text, SourcePathBox.Text, _existingFileId);
        if (error is null && !string.IsNullOrWhiteSpace(SourcePathBox.Text) && !File.Exists(SourcePathBox.Text))
            error = "That PDF no longer exists. Browse to it again.";
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }
        Result = new SubmittedDetails(FileNameBox.Text.Trim(), DirectorDialogValues.Optional(FolderLinkBox.Text), DirectorDialogValues.Optional(SourcePathBox.Text));
        DialogResult = true;
    }
}
