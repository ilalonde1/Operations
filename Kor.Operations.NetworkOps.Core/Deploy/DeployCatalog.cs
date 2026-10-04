#nullable enable
using System.IO;
using System.Linq;

namespace Kor.Operations.NetworkOps.Core.Deploy;

/// <summary>One fleet deployment operation NetworkOps can run on a PC.</summary>
/// <param name="Key">Stable id; recorded in NetworkOps.Actions.Kind.</param>
/// <param name="Disruptive">Interrupts the person at the PC (closes the app/Outlook). Confirmed before a run on an active PC.</param>
/// <param name="PackageSharePath">The V&lt;N&gt;.zip the op installs; the service verifies its SHA-256 at dispatch and the
/// script pulls it from the same share. Null for ops that carry no package.</param>
public sealed record DeployOp(string Key, string Title, string Explain, bool Disruptive, int TimeoutSeconds, string? PackageSharePath = null);

/// <summary>The fleet deployment operations NetworkOps can run on a machine THROUGH ITS AGENT (as SYSTEM) -- the durable
/// delivery channel, since WinRM/RPC are blocked fleet-wide and a one-shot service is killed by the SCM timeout. Each
/// payload is an embedded <c>Deploy/*.ps1</c>; the service fans it out to the ticked machines and records each run like a
/// fix. The app's "Fleet Deploy" view (tick machines -> pick op -> run -> per-device results) is the UI, mirroring the
/// Updates view. This is how app updates and migrations reach the fleet from now on.</summary>
public static class DeployCatalog
{
    public const string MigrateToKorOps = "migrate-to-korops";

    public static readonly IReadOnlyList<DeployOp> All =
    [
        new(MigrateToKorOps, "Migrate to KOR Operations",
            "Moves this PC's install from C:\\Newerforma to C:\\KOR-Operations, re-registers the Outlook add-in (1.0.0.54) to the new path, replaces the stale shortcuts, and removes C:\\Newerforma. Closes the app and Outlook for about a minute; also carries the add-in load-time fix.",
            Disruptive: true, TimeoutSeconds: 900,
            PackageSharePath: @"\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\V24.zip"),
    ];

    public static DeployOp? Get(string key) => All.FirstOrDefault(o => o.Key == key);

    /// <summary>The script that runs on the PC: the op's embedded body, with the dispatcher's values bound on top. The
    /// migrate op needs <c>$Sha</c> (its package's hash, computed by the service from the share), so the agent -- which
    /// runs a script body, not a file with arguments -- gets it as a prepended assignment.</summary>
    public static string Script(DeployOp op, string? sha)
    {
        using var s = typeof(DeployCatalog).Assembly.GetManifestResourceStream($"Deploy.{op.Key}.ps1")
            ?? throw new System.InvalidOperationException($"no embedded deploy script for {op.Key}");
        using var r = new StreamReader(s);
        var body = r.ReadToEnd();
        return sha is { Length: > 0 } ? $"$Sha = '{sha.Replace("'", "''")}'\n{body}" : body;
    }
}
