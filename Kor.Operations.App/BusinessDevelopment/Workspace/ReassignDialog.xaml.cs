#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>
/// Modal picker for reassigning a pursuit: choose (or type) the target owner
/// and an optional reason. The combo is seeded with the owners already holding
/// pursuits but stays editable so a manager can hand it to a new person.
/// </summary>
public partial class ReassignDialog : Window
{
    private readonly string _currentOwner;

    public ReassignDialog(string projectName, string currentOwner, IReadOnlyList<string> knownOwners)
    {
        InitializeComponent();
        _currentOwner = currentOwner;
        HeaderText.Text = $"Reassign “{projectName}” (currently {currentOwner}) to:";
        OwnerCombo.ItemsSource = knownOwners
            .Where(o => !string.Equals(o, currentOwner, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>The chosen target owner (trimmed) once the dialog is accepted.</summary>
    public string TargetOwner { get; private set; } = string.Empty;

    /// <summary>The optional reason, or null.</summary>
    public string? Reason { get; private set; }

    private void Reassign_Click(object sender, RoutedEventArgs e)
    {
        var target = (OwnerCombo.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show(this, "Pick or type an owner to reassign to.", "Reassign", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(target, _currentOwner, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "That is already the owner — pick someone else.", "Reassign", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TargetOwner = target;
        var reason = (ReasonBox.Text ?? string.Empty).Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        DialogResult = true;
    }
}
