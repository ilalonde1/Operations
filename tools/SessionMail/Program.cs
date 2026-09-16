using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

// THE SESSION'S MAIL CHANNEL (2026-09-16). An autonomous session reports progress and asks its blocking
// questions by mail, and reads the lettered answer back. Until today that was two PowerShell scripts in
// %TEMP% (Send-IanMail.ps1, Wait-IanReply.ps1, written for the 2026-09-15 overnight session) - ephemeral,
// against the rule that every instrument is code in the repo. Outlook on this PC, Ian's own profile, no
// secrets, no Graph app: the COM object is bound late so the tool builds without an Outlook reference.
//
//   SessionMail send --subject "..." (--body "..." | --body-file <path>) [--to <address>]
//   SessionMail wait --ticket Q1 --since <ISO time> [--timeout-minutes 480] [--poll-seconds 60] [--from <address>]
//
// wait prints "ANSWER <ticket> at HH:mm:ss: <first non-quoted line of the reply>" and exits 0, or
// "TIMEOUT" and exits 2. Only a REPLY (RE:/AW:/SV:/R:) from the named sender carrying "[PDF-INTAKE <ticket>]"
// in its subject counts - never the question itself.
internal static class Program
{
    private const string DefaultAddress = "ilalonde@korstructural.com";

    [MTAThread]   // a fresh pwsh reaches Outlook from either apartment (measured 2026-09-16); the runtime default
    private static int Main(string[] args)
    {
        if (args.Length == 0) { Usage(); return 1; }
        var opt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Length; i++)
            if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length) opt[args[i][2..]] = args[++i];
        return args[0].ToLowerInvariant() switch
        {
            "send" => Send(opt),
            "wait" => Wait(opt),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: SessionMail send --subject \"...\" (--body \"...\" | --body-file <path>) [--to <address>]");
        Console.Error.WriteLine("       SessionMail wait --ticket Q1 --since <ISO time> [--timeout-minutes 480] [--poll-seconds 60] [--from <address>]");
        return 1;
    }

    private static int Send(Dictionary<string, string> opt)
    {
        if (!opt.TryGetValue("subject", out var subject)) return Usage();
        string? body = opt.TryGetValue("body", out var b) ? b : opt.TryGetValue("body-file", out var f) ? File.ReadAllText(f) : null;
        if (body is null) return Usage();
        string to = opt.TryGetValue("to", out var t) ? t : DefaultAddress;
        try
        {
            dynamic outlook = Outlook();
            dynamic mail = outlook.CreateItem(0);
            mail.To = to;
            mail.Subject = subject;
            mail.Body = body;
            mail.Send();
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80080005)
        {
            // MEASURED 2026-09-16 AND NOT YET EXPLAINED: on this PC an unpackaged .NET 8 process cannot activate Outlook's
            // class factory (CO_E_SERVER_EXEC_FAILURE, STA or MTA, sandbox or not, ROT attach or not) while the Store
            // pwsh (an MSIX package) can, from either apartment. Until the cause is found the send goes through a pwsh
            // child with the same three COM calls - the same profile, the same mailbox, nothing outside this file.
            SendThroughPwsh(to, subject, body);
        }
        Console.WriteLine($"sent {DateTime.Now:HH:mm}: {subject}");
        return 0;
    }

    private static void SendThroughPwsh(string to, string subject, string body)
    {
        string bodyFile = Path.Combine(Path.GetTempPath(), $"SessionMail-{Guid.NewGuid():N}.txt");
        File.WriteAllText(bodyFile, body);
        try
        {
            string script = "$ol = New-Object -ComObject Outlook.Application; $m = $ol.CreateItem(0); $m.To = $env:SM_TO; $m.Subject = $env:SM_SUBJECT; $m.Body = Get-Content -Raw -Path $env:SM_BODY; $m.Send()";
            var psi = new System.Diagnostics.ProcessStartInfo("pwsh", $"-NoProfile -Command \"{script}\"") { UseShellExecute = false, RedirectStandardError = true };
            psi.Environment["SM_TO"] = to; psi.Environment["SM_SUBJECT"] = subject; psi.Environment["SM_BODY"] = bodyFile;
            using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("pwsh did not start");
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException("pwsh could not send: " + err);
        }
        finally { File.Delete(bodyFile); }
    }

    private static int Wait(Dictionary<string, string> opt)
    {
        if (!opt.TryGetValue("ticket", out var ticket) || !opt.TryGetValue("since", out var sinceText)) return Usage();
        var since = DateTime.Parse(sinceText, CultureInfo.InvariantCulture);
        int timeoutMinutes = opt.TryGetValue("timeout-minutes", out var tm) ? int.Parse(tm, CultureInfo.InvariantCulture) : 480;
        int pollSeconds = opt.TryGetValue("poll-seconds", out var ps) ? int.Parse(ps, CultureInfo.InvariantCulture) : 60;
        string from = opt.TryGetValue("from", out var fr) ? fr : DefaultAddress;
        string tag = $"[PDF-INTAKE {ticket}]";
        var deadline = DateTime.Now.AddMinutes(timeoutMinutes);
        var reply = new Regex(@"^\s*(RE|AW|SV|R)\s*:", RegexOptions.IgnoreCase);
        while (DateTime.Now < deadline)
        {
            try
            {
                dynamic outlook = Outlook();
                dynamic items = outlook.GetNamespace("MAPI").GetDefaultFolder(6).Items;
                items.Sort("[ReceivedTime]", true);
                dynamic recent = items.Restrict($"[ReceivedTime] >= '{since.ToString("g", CultureInfo.InvariantCulture)}'");
                foreach (dynamic m in recent)
                {
                    if ((int)m.Class != 43) continue;
                    string subject = (string)m.Subject ?? "";
                    if (!subject.Contains(tag, StringComparison.OrdinalIgnoreCase) || !reply.IsMatch(subject)) continue;
                    if (!string.Equals(SmtpOf(m), from, StringComparison.OrdinalIgnoreCase)) continue;
                    DateTime received = (DateTime)m.ReceivedTime;
                    Console.WriteLine($"ANSWER {ticket} at {received:HH:mm:ss}: {AnswerOf((string)m.Body ?? "")}");
                    return 0;
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { Console.WriteLine($"poll error: {ex.Message}"); }
            Thread.Sleep(TimeSpan.FromSeconds(pollSeconds));
        }
        Console.WriteLine($"TIMEOUT {ticket} after {timeoutMinutes} min");
        return 2;
    }

    private static dynamic Outlook()
    {
        // the RUNNING Outlook, from the running object table: launching a second server instance from a .NET
        // process answers CO_E_SERVER_EXEC_FAILURE on this PC (measured 2026-09-16) where attaching does not
        int hr = CLSIDFromProgID("Outlook.Application", out var clsid);
        if (hr != 0) throw new InvalidOperationException("Outlook is not installed on this PC.");
        if (GetActiveObject(ref clsid, IntPtr.Zero, out var running) == 0 && running is not null) return running;
        var type = Type.GetTypeFromProgID("Outlook.Application") ?? throw new InvalidOperationException("Outlook is not installed on this PC.");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("Outlook is not running and did not start.");
    }

    [DllImport("ole32.dll")] private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);
    [DllImport("oleaut32.dll")] private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object? obj);

    private static string SmtpOf(dynamic mail)
    {
        try { string? s = mail.PropertyAccessor.GetProperty("http://schemas.microsoft.com/mapi/proptag/0x5D01001F"); if (!string.IsNullOrEmpty(s)) return s; } catch (COMException) { }
        try { dynamic u = mail.Sender.GetExchangeUser(); if (u is not null) return (string)u.PrimarySmtpAddress; } catch (COMException) { }
        return (string)mail.SenderEmailAddress;
    }

    // the first non-quoted line of the reply, and the rest of the reply's own lines after it
    private static string AnswerOf(string body)
    {
        var own = new List<string>();
        var quoted = new Regex(@"^(From:|-----Original Message-----|On .* wrote:|Sent from my|________________________________)");
        foreach (var raw in body.Split('\n'))
        {
            string line = raw.Trim();
            if (quoted.IsMatch(line)) break;
            if (line.Length > 0) own.Add(line);
        }
        if (own.Count == 0) return "";
        return own[0] + (own.Count > 1 ? "  [then: " + string.Join(" | ", own.Skip(1)) + "]" : "");
    }
}
