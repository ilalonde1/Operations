#nullable enable
using System.Windows;

namespace Kor.Operations.App.BusinessDevelopment.Director;

public partial class NoteDialog : Window
{
    private readonly bool _required;
    private readonly bool _singleLine;
    public NoteDialog(string title, string prompt, int maxLength = 1000, bool required = false, string? initialNote = null, bool singleLine = false)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        NoteBox.MaxLength = maxLength;
        NoteBox.Text = initialNote ?? "";
        _required = required;
        _singleLine = singleLine;
        if (singleLine) NoteBox.AcceptsReturn = false;
        if (required) NoteBox.SetValue(Kor.Operations.Controls.Placeholder.TextProperty, "Reason (required)");
    }
    public string? Note { get; private set; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Note = DirectorDialogValues.Optional(NoteBox.Text);
        if (_required && string.IsNullOrWhiteSpace(Note))
        {
            MessageBox.Show(this, "Give a reason before saving.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            NoteBox.Focus();
            return;
        }
        if (_singleLine && Note is { } line && (line.Contains('\r') || line.Contains('\n')))
        {
            MessageBox.Show(this, "Keep the reason to one line.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            NoteBox.Focus();
            return;
        }
        DialogResult = true;
    }
}
