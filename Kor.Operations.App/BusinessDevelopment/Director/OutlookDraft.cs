#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.BusinessDevelopment.Director;

/// <summary>
/// Opens a prepared draft in Ian's own Outlook for him to review and send: a message, or an UNSENT meeting invite with its
/// attendees, start and length. Nothing here ever calls Send — the item is only displayed (Ian, 18 October 2026: drafts only).
/// Outlook is reached the way tools/SessionMail reaches it: attach to the running Outlook, and where this PC refuses an
/// unpackaged .NET process (0x80080005, measured 2026-09-16, unexplained) make the same calls from a pwsh child.
/// </summary>
internal static class OutlookDraft
{
    internal static IReadOnlyList<string> Recipients(string to) =>
        to.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static void Open(HourDraft draft)
    {
        try
        {
            dynamic outlook = Outlook();
            dynamic item;
            if (draft.IsInvite && draft.Start is { } start)
            {
                item = outlook.CreateItem(1);          // olAppointmentItem
                item.MeetingStatus = 1;                // olMeeting: an invite with attendees, unsent until Ian sends it
                item.Start = start.ToLocalTime().DateTime;
                item.Duration = draft.Minutes ?? 30;
                item.Location = "Microsoft Teams";
                foreach (var r in Recipients(draft.To)) { dynamic rec = item.Recipients.Add(r); rec.Type = 1; }
                item.Recipients.ResolveAll();
            }
            else
            {
                item = outlook.CreateItem(0);          // olMailItem
                item.To = string.Join(";", Recipients(draft.To));
            }
            item.Subject = draft.Subject;
            item.Body = draft.Body;
            item.Display(false);
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80080005)
        {
            OpenThroughPwsh(draft);
        }
    }

    private static void OpenThroughPwsh(HourDraft draft)
    {
        var file = Path.Combine(Path.GetTempPath(), $"kor-draft-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            invite = draft.IsInvite && draft.Start is not null,
            start = draft.Start?.ToLocalTime().DateTime,   // local wall time; JSON writes it as ISO 8601 with no offset
            minutes = draft.Minutes ?? 30,
            to = Recipients(draft.To),
            subject = draft.Subject,
            body = draft.Body,
        }));
        try
        {
            // The same calls as Open, and the same rule: Display, never Send.
            const string script = "$d = Get-Content -Raw -Path $env:KOR_DRAFT | ConvertFrom-Json; $ol = New-Object -ComObject Outlook.Application; "
                + "if ($d.invite) { $i = $ol.CreateItem(1); $i.MeetingStatus = 1; $i.Start = [datetime]$d.start; $i.Duration = [int]$d.minutes; "
                + "$i.Location = 'Microsoft Teams'; foreach ($r in $d.to) { $x = $i.Recipients.Add($r); $x.Type = 1 }; [void]$i.Recipients.ResolveAll() } "
                + "else { $i = $ol.CreateItem(0); $i.To = ($d.to -join ';') }; $i.Subject = $d.subject; $i.Body = $d.body; $i.Display($false)";
            var psi = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            psi.Environment["KOR_DRAFT"] = file;
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("pwsh did not start.");
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException("Outlook could not open the draft: " + err.Trim());
        }
        finally { File.Delete(file); }
    }

    private static object Outlook()
    {
        if (CLSIDFromProgID("Outlook.Application", out var clsid) != 0) throw new InvalidOperationException("Outlook is not installed on this PC.");
        if (GetActiveObject(ref clsid, IntPtr.Zero, out var running) == 0 && running is not null) return running;
        var type = Type.GetTypeFromProgID("Outlook.Application") ?? throw new InvalidOperationException("Outlook is not installed on this PC.");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("Outlook is not running and did not start.");
    }

    [DllImport("ole32.dll")] private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);
    [DllImport("oleaut32.dll")] private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object? obj);
}
