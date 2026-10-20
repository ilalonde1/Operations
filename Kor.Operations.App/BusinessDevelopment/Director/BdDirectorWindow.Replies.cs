#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using Kor.Operations.Services;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>
/// Record reply on a roll-up: the owner's numbered answers update their pursuits through the existing CRM stores, after Ian
/// confirms the preview (RollupReply holds the rules). Each line is logged as an activity, so the reply is on record and the
/// keep-warm clock restarts; a changed pursuit is saved with UpdateAsync, which writes stage history as every CRM edit does.
/// </summary>
public partial class BdDirectorWindow
{
    private async void RecordReply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HourDraft draft } || !draft.IsRollup) return;
        var items = RollupReply.Items(draft);
        if (items.Count == 0) { SetRhythmStatus("This roll-up carries no pursuit keys, so there is nothing to record against."); return; }
        try
        {
            var store = AppServices.Get<ICrmEngagementStore>();
            var titles = new Dictionary<long, string>();
            foreach (var item in items)
                if (await store.GetByIdAsync(item.EngagementId, _lifetime.Token).ConfigureAwait(true) is { } found)
                    titles[item.EngagementId] = PursuitTitle(found);

            var zone = MeetingsDue.Pacific();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.Now, zone).Date);
            var dialog = new ReplyDialog(draft.RollupName, items, titles, today) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            var actor = CurrentActor();
            var activities = AppServices.Get<ICrmActivityStore>();
            var saved = 0;
            foreach (var action in dialog.Actions)
            {
                var current = await store.GetByIdAsync(action.Item.EngagementId, _lifetime.Token).ConfigureAwait(true);
                if (current is null) continue;
                var updated = RollupReply.Apply(current, action, zone);
                if (!ReferenceEquals(updated, current)) await store.UpdateAsync(updated, actor, _lifetime.Token).ConfigureAwait(true);
                await activities.AppendAsync(RollupReply.Activity(action, draft.RollupName, actor, DateTimeOffset.UtcNow), actor, _lifetime.Token).ConfigureAwait(true);
                saved++;
            }
            SetRhythmStatus($"Recorded {saved} of {dialog.Actions.Count} answers from {draft.RollupName}.");
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { /* Window closed. */ }
        catch (Exception ex)
        {
            Log.Warning(ex, "Recording a roll-up reply failed.");
            SetRhythmStatus("The reply could not be recorded: " + ex.Message);
        }
    }

    private static string PursuitTitle(Kor.Opportunities.Core.Models.CrmEngagement e)
    {
        var project = e.PotentialProjects?.Trim();
        var buyer = e.BuyerOrgName?.Trim();
        return string.IsNullOrEmpty(project) ? buyer ?? "Pursuit #" + e.Id
            : string.IsNullOrEmpty(buyer) || string.Equals(project, buyer, StringComparison.OrdinalIgnoreCase) ? project : $"{project} ({buyer})";
    }
}
