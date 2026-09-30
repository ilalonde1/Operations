#nullable enable
using Kor.Operations.NetworkOps.Core.Power;
using Kor.Operations.NetworkOps.Service.Alerting;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Power;

// Polls both UPS cards every PowerPollSeconds over SNMPv3, keeps the history, decides with PowerPolicy,
// and on a Trigger runs the shutdown chain once -- REAL only when PowerChainArmed, a dry run otherwise.
// Every change of verdict is an event and a mail. This loop must never die of a bad poll: every tick
// is caught, and a card that does not answer is itself a reading.
internal sealed class PowerWatchService(IOptions<NetworkOpsOptions> options, PowerState state, NetworkOpsStore store, PowerChainRunner chain,
    IDigestSender alerts, ILogger<PowerWatchService> log) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _lastStored = new(StringComparer.OrdinalIgnoreCase);
    private bool _chainStarted;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (o.Ups.Count == 0 || string.IsNullOrWhiteSpace(o.SnmpUser) || string.IsNullOrWhiteSpace(o.SnmpAuthPassword) || string.IsNullOrWhiteSpace(o.SnmpPrivPassword))
        {
            log.LogInformation("UPS watcher off: Ups and the KOR_NETWORKOPS_SNMP* credentials are not all set");
            return;
        }
        try
        {
            if (!await store.PowerSchemaPresentAsync(ct).ConfigureAwait(false))
            {
                log.LogError("UPS watcher NOT started: run db/KorNetworkOps/004_RackPower.sql as sa, then restart the service");
                return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "UPS watcher NOT started: cannot reach the database"); return; }
        state.Configured = true;
        log.LogInformation("UPS watcher on: {Cards}, every {Seconds} s; chain {Armed}", string.Join(", ", o.Ups.Select(u => $"{u.Name} {u.Address}")),
            o.PowerPollSeconds, o.PowerChainArmed ? "ARMED" : "NOT armed (a trigger runs a dry run)");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, o.PowerPollSeconds)));
        do
        {
            try { await TickAsync(o, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning(ex, "UPS watch tick failed"); }
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task TickAsync(NetworkOpsOptions o, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var readings = await Task.WhenAll(o.Ups.Select(card => ReadAsync(card, o, now, ct))).ConfigureAwait(false);
        foreach (var (card, r) in o.Ups.Zip(readings))
        {
            var changed = state.Update(r, card.Address, now);
            if (changed || !_lastStored.TryGetValue(card.Name, out var last) || now - last >= TimeSpan.FromSeconds(o.PowerRecordSeconds))
            {
                try { await store.RecordPowerReadingAsync(r, ct).ConfigureAwait(false); _lastStored[card.Name] = now; }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Could not store a UPS reading"); }
            }
        }

        var verdict = PowerPolicy.Evaluate(state.Views(), now,
            new PowerPolicySettings(o.PowerOnBatteryMinutes, o.PowerRuntimeFloorMinutes, o.PowerBlindAfterSeconds));
        var previous = state.Verdict;
        if (state.SetVerdict(verdict, now))
        {
            log.LogWarning("Power verdict {From} -> {To}: {Reason}", previous.Level, verdict.Level, verdict.Reason);
            await store.AddPowerEventAsync(null, "Verdict", !o.PowerChainArmed, verdict.Level != PowerLevel.Trigger, $"{previous.Level} -> {verdict.Level}: {verdict.Reason}").ConfigureAwait(false);
            // The first verdict after a start is "no reading yet -> X": not news unless X is bad.
            if (!(previous.Reason == "no reading yet" && verdict.Level == PowerLevel.Normal))
            {
                using var cap = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await alerts.SendAlertAsync($"[NetworkOps] rack power: {verdict.Level.ToString().ToUpperInvariant()}", verdict.Reason, cap.Token).ConfigureAwait(false); }
                catch (Exception ex) { log.LogWarning(ex, "Power alert not sent"); }
            }
        }

        if (verdict.Level == PowerLevel.Normal) _chainStarted = false;   // the episode is over; a new outage may start a new chain
        if (verdict.Level == PowerLevel.Trigger && !_chainStarted)
        {
            _chainStarted = true;
            var dry = !o.PowerChainArmed;
            // Not awaited: the watcher keeps reading (and recording) the UPSes while the chain runs.
            _ = Task.Run(async () =>
            {
                try
                {
                    var outcome = await chain.RunAsync(dry, verdict.Reason, CancellationToken.None).ConfigureAwait(false);
                    log.LogWarning("Power chain ({Mode}) ended: {Summary}", dry ? "dry run" : "REAL", outcome.Summary);
                }
                catch (Exception ex) { log.LogError(ex, "Power chain crashed"); }
            }, CancellationToken.None);
        }
    }

    private static async Task<UpsReading> ReadAsync(UpsCard card, NetworkOpsOptions o, DateTime now, CancellationToken ct)
    {
        try
        {
            var creds = new SnmpV3Credentials(o.SnmpUser, o.SnmpAuthPassword, o.SnmpPrivPassword, card.AuthSha256);
            var values = await SnmpChannel.GetAsync(card.Address, creds, UpsMibs.OidsFor(card.Mib), TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
            return UpsMibs.Parse(card.Name, card.Mib, now, values);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return UpsReading.Unreachable(card.Name, now, ex.GetType().Name + ": " + ex.Message);
        }
    }
}
