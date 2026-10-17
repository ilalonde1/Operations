#nullable enable
using System;
using System.Windows;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.Crm;

/// <summary>Shared close gate for the Pursuits stage menu and engagement editor, including Director links into CRM.</summary>
public static class PursuitCloseReason
{
    public static bool IsClosed(CrmEngagementStage stage) =>
        stage is CrmEngagementStage.Won or CrmEngagementStage.Lost or CrmEngagementStage.Archived;

    private static bool IsValid(string? line) =>
        !string.IsNullOrWhiteSpace(line) && line.Length <= 2000 && !line.Contains('\r') && !line.Contains('\n');

    public static string Require(string? reason)
    {
        var line = reason?.Trim();
        if (!IsValid(line))
            throw new ArgumentException("Give a one-line reason for closing the pursuit (up to 2000 characters).", nameof(reason));
        return line!;
    }

    /// <summary>Null means cancel: the caller must not save. The injected prompt makes cancellation testable without a window.</summary>
    public static CrmEngagement? Prepare(CrmEngagement current, CrmEngagementStage stage, Func<string, string?> prompt)
    {
        if (stage == current.Stage && (!IsClosed(stage) || (current.Id != 0 && IsValid(current.OutcomeReason)))) return current;
        if (!IsClosed(stage)) return current with { Stage = stage, OutcomeReason = null };
        var reason = prompt($"Why is this pursuit {CrmStageNames.Display(stage).ToLowerInvariant()}? Give one line.");
        return reason is null ? null : current with { Stage = stage, OutcomeReason = Require(reason) };
    }

    public static CrmEngagement? Prompt(Window? owner, CrmEngagement current, CrmEngagementStage stage) =>
        Prepare(current, stage, question =>
        {
            var dialog = new NoteDialog("Close the pursuit", question, 2000, required: true, singleLine: true) { Owner = owner };
            return dialog.ShowDialog() == true ? dialog.Note : null;
        });
}
