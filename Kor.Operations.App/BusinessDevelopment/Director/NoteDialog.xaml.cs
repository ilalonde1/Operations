#nullable enable
using System.Windows;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public partial class NoteDialog : Window
{
    private readonly bool _required;
    public NoteDialog(string title, string prompt, int maxLength = 1000, bool required = false)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        NoteBox.MaxLength = maxLength;
        _required = required;
        if (required) NoteBox.SetValue(Kor.Operations.Controls.Placeholder.TextProperty, "Reason (required)");
    }
    public string? Note { get; private set; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Note = DirectorDialogValues.Optional(NoteBox.Text);
        if (_required && string.IsNullOrWhiteSpace(Note))
        {
            MessageBox.Show(this, "Give a reason before retiring the claim.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            NoteBox.Focus();
            return;
        }
        DialogResult = true;
    }
}
