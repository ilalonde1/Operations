#nullable enable
using Kor.Operations.NetworkOps.Core.Actions;

namespace Kor.Operations.NetworkOps.Service.Updates;

/// <summary>One ticked machine, as the batch rules see it.</summary>
/// <param name="PresenceState">Active | Locked | RemoteOnly | Nobody | null (unknown, e.g. a server).</param>
/// <param name="InstallInFlight">An install is already Requested or Running on it.</param>
internal sealed record BatchMember(UpdateTarget Target, string? PresenceState, string? Presence, bool InstallInFlight);

/// <summary>What happens to one machine: the fix to queue, or why not.</summary>
internal sealed record BatchDecision(int DeviceId, string Name, string? FixId, string? Refused, bool NeedsConfirmation, string? Note);

// The rules for "install updates on these machines", in one place, enforced by the API (not only the page):
//   - a machine NetworkOps cannot run anything on is refused with the reason it was listed with;
//   - the domain controller (UpdateAloneHosts) only goes in a batch of its own: DNS, DHCP and sign-in go with it;
//   - "restart if needed" never restarts APP01 (UpdateNoRestartHosts): it installs there without the restart, and says so;
//   - "restart if needed" on ANY machine is refused until confirmed (the rule for every disruptive fix; the runner enforces it too);
//   - a machine already installing is not given a second install.
internal static class UpdateBatch
{
    public static IReadOnlyList<BatchDecision> Decide(IReadOnlyList<BatchMember> members, bool restartIfNeeded, bool confirmed)
    {
        var batch = members.Count;
        return members.Select(m =>
        {
            var t = m.Target;
            if (t.Host is null)
                return new BatchDecision(t.DeviceId, t.Name, null, t.Why ?? "NetworkOps cannot run anything on it", false, null);
            if (m.InstallInFlight)
                return new BatchDecision(t.DeviceId, t.Name, null, "an install is already queued or running on it", false, null);
            if (t.Guard == "Alone" && batch > 1)
                return new BatchDecision(t.DeviceId, t.Name, null,
                    $"{t.Name} is patched in a batch of its own (sign-in, DNS and DHCP stop while it restarts): untick the others, or do it after them", false, null);
            if (!restartIfNeeded)
                return new BatchDecision(t.DeviceId, t.Name, FixCatalog.InstallUpdates, null, false, null);
            if (t.Guard == "NoRestart")
                return new BatchDecision(t.DeviceId, t.Name, FixCatalog.InstallUpdates, null, false,
                    $"installs without the restart: {t.Name} runs NetworkOps and is never restarted from here -- restart it yourself when convenient");
            // EVERY disruptive install needs explicit confirmation -- the same rule as a fix (ApiHost, audit 2026-10-06 #1) and
            // the runner's last gate. Presence shapes the warning, never waives it. (Asking only when Active queued the restart
            // variant unconfirmed on every idle, locked or unknown machine, and the runner refused each one: audit 2026-10-16, 1.1.)
            if (!confirmed)
                return new BatchDecision(t.DeviceId, t.Name, null, m.PresenceState == "Active"
                    ? $"someone is using it right now ({m.Presence}): confirm to restart it if an update needs it"
                    : "it restarts if an update needs it: confirm to go ahead", true, null);
            return new BatchDecision(t.DeviceId, t.Name, FixCatalog.InstallUpdatesRestart, null, false, null);
        }).ToList();
    }
}
