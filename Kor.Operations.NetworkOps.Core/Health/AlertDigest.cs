#nullable enable
using System.Net;
using System.Text;

namespace Kor.Operations.NetworkOps.Core.Health;

/// <summary>One device's notifiable changes from a sweep.</summary>
public sealed record DeviceChanges(string Device, IReadOnlyList<FindingChange> Changes);

public sealed record DigestMessage(string Subject, string HtmlBody);

// Composes the one email a sweep produces. Critical first, then Warning, then what cleared --
// machine by machine, with the evidence that raised each finding, so the email is actionable
// without opening anything. Returns null when nothing is worth sending: no news is no email.
public static class AlertDigest
{
    public static DigestMessage? Compose(IReadOnlyList<DeviceChanges> devices, DateTime sweptAtLocal)
        => Compose(devices, [], sweptAtLocal);

    /// <param name="newPatterns">Fleet patterns found for the first time this sweep (the "knows about other PCs" layer).</param>
    public static DigestMessage? Compose(IReadOnlyList<DeviceChanges> devices, IReadOnlyList<Learning.FleetInsight> newPatterns, DateTime sweptAtLocal)
    {
        var raised = devices.SelectMany(d => d.Changes.Where(c => c.Kind is ChangeKind.New or ChangeKind.Escalated).Select(c => (d.Device, c))).ToList();
        var cleared = devices.SelectMany(d => d.Changes.Where(c => c.Kind == ChangeKind.Cleared).Select(c => (d.Device, c))).ToList();
        if (raised.Count == 0 && cleared.Count == 0 && newPatterns.Count == 0) return null;

        var critical = raised.Count(x => x.c.Current!.Severity == Severity.Critical);
        var subject = new StringBuilder("[NetworkOps] ");
        if (critical > 0) subject.Append($"{critical} critical, ");
        subject.Append($"{raised.Count} new or worse, {cleared.Count} cleared");
        if (newPatterns.Count > 0) subject.Append($", {newPatterns.Count} fleet pattern{(newPatterns.Count == 1 ? "" : "s")}");
        subject.Append($" -- {sweptAtLocal:ddd d MMM HH:mm}");

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px\">");
        if (newPatterns.Count > 0)
        {
            html.Append("<h3 style=\"margin:16px 0 6px\">Fleet patterns</h3><ul style=\"margin:0\">");
            foreach (var p in newPatterns.OrderBy(p => p.PValue))
                html.Append("<li>").Append(Enc(p.Summary)).Append(" <span style=\"color:#555\">(")
                    .Append(Enc(string.Join(", ", p.Devices))).Append(")</span></li>");
            html.Append("</ul>");
        }
        foreach (var group in raised.OrderByDescending(x => x.c.Current!.Severity).ThenBy(x => x.Device, StringComparer.OrdinalIgnoreCase)
                                    .GroupBy(x => x.c.Current!.Severity))
        {
            html.Append($"<h3 style=\"margin:16px 0 6px\">{group.Key}</h3><table style=\"border-collapse:collapse\">");
            foreach (var (device, c) in group)
            {
                var tag = c.Kind == ChangeKind.Escalated ? $" <i>(was {c.Previous!.Severity})</i>" : "";
                html.Append("<tr><td style=\"padding:3px 12px 3px 0;vertical-align:top\"><b>").Append(Enc(device)).Append("</b></td>")
                    .Append("<td style=\"padding:3px 0\">").Append(Enc(c.Current!.Title)).Append(tag)
                    .Append("<br><span style=\"color:#555\">").Append(Enc(c.Current.Evidence)).Append("</span></td></tr>");
            }
            html.Append("</table>");
        }
        if (cleared.Count > 0)
        {
            html.Append("<h3 style=\"margin:16px 0 6px\">Cleared</h3><ul style=\"margin:0\">");
            foreach (var (device, c) in cleared.OrderBy(x => x.Device, StringComparer.OrdinalIgnoreCase))
                html.Append("<li>").Append(Enc(device)).Append(": ").Append(Enc(c.RuleKey)).Append("</li>");
            html.Append("</ul>");
        }
        html.Append($"<p style=\"color:#777;font-size:12px;margin-top:18px\">NetworkOps sweep {sweptAtLocal:yyyy-MM-dd HH:mm}. " +
                    "You get this once when a finding appears or gets worse, and once when it clears.</p></div>");
        return new DigestMessage(subject.ToString(), html.ToString());
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
