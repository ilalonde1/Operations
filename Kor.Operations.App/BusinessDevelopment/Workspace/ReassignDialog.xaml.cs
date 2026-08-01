#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

/// <summary>One selectable person for the reassign picker — display name + email.</summary>
public sealed record PersonOption(string Display, string Email);

/// <summary>
/// Modal picker for reassigning a pursuit: choose a person from the team
/// directory (AD-sourced, name shown) and an optional reason. The combo stays
/// editable so a manager can type an address not yet in the directory.
/// </summary>
public partial class ReassignDialog : Window
{
    private readonly string _currentOwner;

    public ReassignDialog(string projectName, string currentOwner, IReadOnlyList<PersonOption> people)
    {
        InitializeComponent();
        _currentOwner = currentOwner;
        HeaderText.Text = $"Reassign “{projectName}” (currently {currentOwner}) to:";
        OwnerCombo.ItemsSource = people
            .Where(p => !string.Equals(p.Email, currentOwner, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(p.Display, currentOwner, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>The owner string to store (the target's email). Set on accept.</summary>
    public string TargetOwner { get; private set; } = string.Empty;

    /// <summary>The target's email for the notification.</summary>
    public string TargetEmail { get; private set; } = string.Empty;

    /// <summary>Friendly name for status/messages.</summary>
    public string TargetDisplay { get; private set; } = string.Empty;

    /// <summary>The optional reason, or null.</summary>
    public string? Reason { get; private set; }

    private void Reassign_Click(object sender, RoutedEventArgs e)
    {
        string email;
        string display;

        if (OwnerCombo.SelectedItem is PersonOption picked)
        {
            email = picked.Email;
            display = picked.Display;
        }
        else
        {
            // Free-typed value: treat it as the address.
            var typed = (OwnerCombo.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(typed))
            {
                MessageBox.Show(this, "Pick a person, or type an email address.", "Reassign", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            email = typed;
            display = typed;
        }

        if (string.Equals(email, _currentOwner, StringComparison.OrdinalIgnoreCase)
            || string.Equals(display, _currentOwner, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "That is already the owner — pick someone else.", "Reassign", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TargetEmail = email;
        TargetDisplay = display;
        TargetOwner = email;
        var reason = (ReasonBox.Text ?? string.Empty).Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        DialogResult = true;
    }
}
