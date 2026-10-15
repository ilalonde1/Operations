#nullable enable
using System.Windows;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public partial class NoteDialog : Window
{
    public NoteDialog(string title, string prompt, int maxLength = 1000)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        NoteBox.MaxLength = maxLength;
    }
    public string? Note { get; private set; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Note = DirectorDialogValues.Optional(NoteBox.Text);
        DialogResult = true;
    }
}
