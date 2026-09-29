#nullable enable
namespace Kor.Operations.NetworkOps.Core.Learning;

/// <param name="Meaning">What the finding says, in one or two plain sentences.</param>
/// <param name="Fixes">Known fixes, most likely first -- each learned on this fleet where it says so.</param>
/// <param name="IfIgnored">What happens if nobody acts.</param>
public sealed record KnowledgeEntry(string Family, string Meaning, IReadOnlyList<string> Causes, IReadOnlyList<string> Fixes, string IfIgnored);

// What each kind of finding means and what has fixed it, written from KOR's own 2026 diagnoses rather
// than generic advice. Learned fixes (FixLearning) are shown beside these as the fleet accumulates
// evidence. A test fails if any rule can raise a family that has no entry here.
public static class Knowledge
{
    public static KnowledgeEntry? For(string ruleKey) => Entries.TryGetValue(FixLearning.FamilyOf(ruleKey), out var e) ? e : null;

    public static IReadOnlyCollection<string> Families => Entries.Keys;

    private static readonly Dictionary<string, KnowledgeEntry> Entries = new[]
    {
        E("wmi-broken", "Windows Management Instrumentation is not answering, so most health reads fail and the machine is partly blind to monitoring.",
            ["A corrupted WMI repository (KOR-213, Sep 2026)"],
            ["Run `winmgmt /verifyrepository`; if inconsistent, `winmgmt /salvagerepository`", "Restart the Winmgmt service", "Last resort: `winmgmt /resetrepository` and reboot"],
            "Inventory, disk health and most checks stay blind on this PC; faults will not be seen."),
        E("disk-missing", "A drive Windows used to see is no longer present, and any drive letter it had now points at nothing.",
            ["The drive has failed (KOR-206-N's Samsung 870 EVO, Sep 2026, after bad blocks and resets)", "A loose SATA data or power cable"],
            ["Reseat the SATA data and power cables", "If it returns: copy its data off at once, then replace it", "If not: data recovery from the drive in a USB adapter"],
            "Whatever was only on that drive is lost."),
        E("disk-failing", "A drive is reporting bad blocks, file-system corruption or repeated controller resets -- the classic run-up to failure.",
            ["Failing flash or platters", "A marginal cable or port (resets without bad blocks)"],
            ["Copy its data to a safe place now", "Replace the drive", "If only resets: try another cable and SATA port first"],
            "The drive can vanish without warning, as 206-N's D: did three weeks after its first bad blocks."),
        E("gpu-hangs", "The graphics driver keeps timing out and resetting (video engine timeout, 0x141). Users see freezes and black flashes.",
            ["The Lenovo ThinkStation P340 -- 9 of 10 hang, against 6 of 19 other PCs (fleet comparison, Sep 2026); the model, not one driver version, is what the fleet data points at","A card that is failing or dropping off the bus (104N shows no NVIDIA card at all)", "A card not seated properly"],
            ["Install the current NVIDIA workstation driver for that card (clean install)", "Reseat the card and its power connector", "Replace an old Quadro that still hangs on a current driver"],
            "Constant freezes in Revit and CAD; eventually the card drops out entirely."),
        E("unexpected-shutdowns", "The PC is losing power or crashing hard without a clean shutdown.",
            ["A blue-screen crash", "Power loss or a failing PSU", "Overheating"],
            ["Look up the bug-check in the System log around the time", "Check the PSU and power cable; move it to a UPS outlet"],
            "Open work is lost each time, and a crash can corrupt files."),
        E("hardware-errors", "The processor, memory or PCIe bus is reporting hardware errors (WHEA).",
            ["A failing component", "A device misbehaving on the PCIe bus (often a disk or GPU)"],
            ["Read the WHEA events to see which component", "Update the BIOS", "Replace the named component"],
            "Hardware errors precede crashes and data corruption."),
        E("crash-loop", "An application keeps crashing.",
            ["opushutil.exe: Office on machines with the Access Database Engine (13 PCs, 2026)", "Revit/Bluebeam: version-specific faults (207, 308)", "A corrupt profile or add-in"],
            ["Update the application", "Repair Office for opushutil", "Reset the app's user profile/preferences"],
            "The user loses work and time with every crash."),
        E("outlook-index", "Windows Search cannot index a mailbox file, so Outlook search misses mail.",
            ["A damaged mailbox store -- a rebuild did NOT fix it on 206-N (Jul and Sep 2026)"],
            ["Recreate the Outlook profile (new OST) rather than rebuilding the index again"],
            "Searches silently miss results."),
        E("office-access-engine-stale", "The Access Database Engine left 2020-era Office DLLs that hijack Click-to-Run Office.",
            ["Access Database Engine 2016 redistributable, unpatched"],
            ["Update the redistributable (KB5002623 or later) -- never delete the folder, Excel/Access data access depends on it", "Turn on Microsoft Update so Office MSI patches arrive"],
            "Office components keep crashing daily."),
        E("low-disk", "A drive is nearly full.",
            ["Growing project caches, temp files, old installers, a large mailbox file"],
            ["Clear temp and old installers", "Move project data to the server", "Add or enlarge the drive"],
            "Saves and syncs to that drive start failing when it fills; if it is C:, Windows updates and most applications fail too."),
        E("reboot-overdue", "Updates are installed but waiting on a restart, and the PC has been up for days.",
            ["The PC is never restarted"], ["Restart it"],
            "Security fixes are not active until the restart."),
        E("not-patched", "No Windows update has installed in over a month.",
            ["Updates failing", "Update scheduling off (Ninja's NoAutoUpdate policy with nothing driving updates)"],
            ["Check Windows Update history for failures", "Run the update manually"],
            "Known security holes stay open."),
        E("microsoft-update-off", "Microsoft Update is not registered, so Office MSI patches (Access Database Engine) are never offered.",
            ["Only Windows Update was ever enabled"], ["Opt in to Microsoft Update (done fleet-wide 28 Sep 2026)"],
            "Office components stay unpatched."),
        E("fan-profile-loud", "The BIOS fan profile holds the fans high even when the PC is idle and cool.",
            ["Lenovo 'Performance' or 'Best Performance' cooling mode (KOR-305, 206-N)"],
            ["Set IntelligentCoolingPerformanceMode to 'Balance mode' (applies after a restart)", "If still loud when idle and cool: a dusty or failing fan (305's rear exhaust fan)"],
            "Noise complaints; no thermal risk."),
        E("unbacked-data", "Real data sits on a drive that no backup covers.",
            ["Work kept on a second local drive (13 months of 206-N's work lived only on D:)"],
            ["Move the data into OneDrive or onto the server", "Turn on OneDrive known-folder backup"],
            "If the drive fails, the data is gone -- as it nearly was on 206-N."),
        E("remote-tools-stale", "Older generations of remote-access agents are still installed and running as SYSTEM.",
            ["MSP agents from 2022 and 2023 never uninstalled"], ["Uninstall the older ScreenConnect instances"],
            "Old remote-access software is a known target."),
        E("newforma-residue", "Newforma leftovers remain after its removal.",
            ["Uninstall missed the PDF component and per-user caches"], ["Uninstall Newforma PDF; delete AppData\\Local\\Newforma"],
            "Webroot keeps flagging the leftover DLLs."),
        E("probe-incomplete", "Part of the health check could not read this PC, so its picture is incomplete.",
            ["A component the probe reads is missing or broken on this machine"], ["Look at the named block; fix the component"],
            "Faults in the unread part go unseen."),
        E("device-silent", "The PC has not answered for days.",
            ["Switched off, disconnected, off the VPN, or retired"], ["Confirm with its user; retire it in NetworkOps if it is gone"],
            "Nothing is known about it -- silence is itself the finding."),
        E("disk-filling", "At the current rate a drive will run out of space soon.",
            ["Steady growth of caches, projects or mail"], ["Clear space or move data before the date", "Find what is growing"],
            "Applications and updates fail on the day it fills."),
        E("mailbox-near-limit", "An Outlook mailbox file is approaching Outlook's 50 GB ceiling.",
            ["A large mailbox cached in full"], ["Archive old mail", "Limit the cached period (Cached Exchange Mode slider)", "Raise the OST limit by policy"],
            "At 50 GB Outlook stops syncing and mail stops arriving in the client."),
        E("disk-wearing", "An SSD is using up its rated write endurance.",
            ["Heavy writes over years"], ["Plan a replacement", "Replace above 90%"],
            "A worn-out SSD can go read-only or fail."),
        E("disk-errors", "A drive has started logging read or write errors.",
            ["Failing media", "A bad cable (for SATA)"], ["Copy the data off", "Replace the drive"],
            "Errors grow into failure; unrecoverable reads mean data is already being lost."),
        E("disk-aging", "An old spinning hard drive is still in service.",
            ["Original drive from a long-lived PC"], ["Move its data off and replace it with an SSD"],
            "Hard drives fail more often past about five years."),
        E("gpu-hangs-rising", "Graphics hangs are increasing week on week.", ["See gpu-hangs"], ["See gpu-hangs"], "Heading toward constant freezes."),
        E("app-hangs-rising", "Programs are freezing more often than last week.", ["Memory pressure, a failing disk, an add-in"], ["Check memory and disk findings on this PC"], "Freezes turn into crashes."),
        E("memory-pressure-rising", "Windows is running low on memory more often.", ["Too many heavy apps open at once", "A memory leak"], ["Close unused apps; add RAM; enlarge the page file"], "Apps start failing and crashing."),
        E("disk-resets-rising", "Drive controller resets are increasing.", ["See disk-failing"], ["See disk-failing"], "A drive about to drop out."),
        E("hardware-errors-rising", "Hardware errors are increasing.", ["See hardware-errors"], ["See hardware-errors"], "Crashes and corruption."),
        E("crashes-rising", "An application is crashing more often than last week.", ["A recent update or add-in"], ["See crash-loop"], "More lost work."),
        E("boot-slowing", "The PC takes much longer to start than it used to.", ["A failing disk", "Startup programs", "Group Policy waiting on a dead share"], ["Check disk health; review startup items; check GPO drive maps"], "Slow starts often precede disk trouble."),
        E("battery-worn", "The laptop battery holds much less charge than new.", ["Age and cycles"], ["Replace the battery"], "Short runtime; a worn battery can swell."),
        E("os-unsupported", "The PC runs Windows 10, which stopped receiving security updates on 14 Oct 2025.", ["Not yet upgraded, or hardware that cannot run Windows 11"], ["Upgrade to Windows 11, or replace the PC"], "Every new Windows vulnerability stays open on it."),
        E("not-restarted", "The PC has not been restarted in over a month.", ["Left on and locked"], ["Restart it"], "Pending fixes never apply; small leaks accumulate."),
    }.ToDictionary(e => e.Family, StringComparer.Ordinal);

    private static KnowledgeEntry E(string family, string meaning, string[] causes, string[] fixes, string ifIgnored)
        => new(family, meaning, causes, fixes, ifIgnored);
}
