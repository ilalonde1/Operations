#nullable enable
using System.Windows;

namespace Kor.Operations.StandardDetails;

public partial class GroupEditWindow : Window
{
    public string GroupName { get; private set; } = string.Empty;

    public GroupEditWindow(string title, string prompt, string initialName = "")
    {
        InitializeComponent();
        Title = title;
        WindowTitleText.Text = title;
        PromptText.Text = prompt;
        GroupNameBox.Text = initialName;
        GroupNameBox.Focus();
        GroupNameBox.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = GroupNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Group name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        GroupName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
