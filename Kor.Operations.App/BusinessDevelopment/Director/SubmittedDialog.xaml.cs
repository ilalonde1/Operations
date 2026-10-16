#nullable enable
using System.Windows;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public sealed record SubmittedDetails(string FileName, string? FolderLink)
{
    internal static string? Validate(string? fileName, string? folderLink)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "Enter the submitted file name.";
        if (fileName.Length > 260) return "The file name must be 260 characters or fewer.";
        if (folderLink?.Length > 1000) return "The folder link must be 1000 characters or fewer.";
        return null;
    }
}

public partial class SubmittedDialog : Window
{
    public SubmittedDialog(string? fileName = null, string? folderLink = null)
    {
        InitializeComponent();
        FileNameBox.Text = fileName ?? "";
        FolderLinkBox.Text = folderLink ?? "";
    }

    public SubmittedDetails? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var error = SubmittedDetails.Validate(FileNameBox.Text, FolderLinkBox.Text);
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }
        Result = new SubmittedDetails(FileNameBox.Text.Trim(), DirectorDialogValues.Optional(FolderLinkBox.Text));
        DialogResult = true;
    }
}
