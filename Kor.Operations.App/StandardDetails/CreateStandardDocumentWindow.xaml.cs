#nullable enable
using System.Windows;

namespace Kor.Operations.StandardDetails;

public partial class CreateStandardDocumentWindow : Window
{
    private const int TitleMaxLength = 300;
    private const int DescriptionMaxLength = 2000;

    public string DocumentTitle { get; private set; } = string.Empty;
    public string? DocumentDescription { get; private set; }

    public CreateStandardDocumentWindow()
    {
        InitializeComponent();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show(this, "Title is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (title.Length > TitleMaxLength)
        {
            MessageBox.Show(this, $"Title cannot exceed {TitleMaxLength} characters.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();
        if (description is not null && description.Length > DescriptionMaxLength)
        {
            MessageBox.Show(this, $"Description cannot exceed {DescriptionMaxLength} characters.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DocumentTitle = title;
        DocumentDescription = description;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
