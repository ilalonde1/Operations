#nullable enable
using System.Windows;

namespace Kor.Operations.StandardDetails;

/// <summary>
/// Retiring a detail, with the reason that detail.RetireDetail requires.
///
/// KOR-D-00003 was retired by a hand-edit in SQL with RetiredReason NULL and no record of who did
/// it. Nobody can now say why. The reason is enforced in the procedure, not only asked for here.
/// </summary>
public partial class RetireDetailWindow : Window
{
    internal string Reason { get; private set; } = string.Empty;

    internal RetireDetailWindow(string detailNumber, string title)
    {
        InitializeComponent();
        WindowTitleText.Text = $"Retire {detailNumber}";
        PromptText.Text = title;
        ReasonBox.Focus();
    }

    private void Retire_Click(object sender, RoutedEventArgs e)
    {
        var reason = ReasonBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show(this,
                "A reason is required. A detail that vanishes with no reason cannot be defended to the engineer who used it.",
                "Standard Details — Retire", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Reason = reason;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
