#nullable enable
using System.Collections.Concurrent;
using Kor.Operations.NetworkOps.Core.Bios;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.NetworkOps.Service.Bios;

// Lenovo's current BIOS packages per machine type, read by APP01 from Lenovo's public catalog at most once a day per type
// (KOR has a handful of types, so a fleet sweep costs a few requests a day). The health sweep judges each Lenovo PC against
// it (Core/Bios/BiosRules). A catalog that cannot be read is UNKNOWN (null), never "no update": the sweep then leaves the
// PC's open BIOS finding exactly as it was. A read older than a week is not used at all.
internal sealed class LenovoBiosCatalog(ILogger<LenovoBiosCatalog> log, TimeProvider clock, HttpMessageHandler? handler = null)
{
    private readonly HttpClient _client = new(handler ?? new HttpClientHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };

    public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(24);
    public static readonly TimeSpan UsableFor = TimeSpan.FromDays(7);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

    private sealed record Entry(IReadOnlyList<LenovoBiosPackage>? Packages, DateTime ReadUtc, DateTime TriedUtc);
    private readonly ConcurrentDictionary<string, Entry> _byType = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The packages for a machine type, or null when Lenovo's catalog has not been read successfully within a week.</summary>
    public async Task<IReadOnlyList<LenovoBiosPackage>?> PackagesAsync(string machineType, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (_byType.TryGetValue(machineType, out var e) && (now - e.ReadUtc < RefreshAfter || now - e.TriedUtc < RetryAfter))
            return e.Packages is not null && now - e.ReadUtc < UsableFor ? e.Packages : null;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_byType.TryGetValue(machineType, out e) && (now - e.ReadUtc < RefreshAfter || now - e.TriedUtc < RetryAfter))
                return e.Packages is not null && now - e.ReadUtc < UsableFor ? e.Packages : null;
            try
            {
                var packages = await ReadAsync(machineType, ct).ConfigureAwait(false);
                _byType[machineType] = new Entry(packages, now, now);
                log.LogInformation("Lenovo BIOS catalog {Type}: {Packages}", machineType, string.Join(", ", packages.Select(p => $"{p.Id} {p.Version}")));
                return packages;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or System.Xml.XmlException)
            {
                log.LogWarning("Lenovo BIOS catalog {Type} could not be read: {Error}", machineType, ex.Message);
                var kept = e ?? new Entry(null, default, now);
                _byType[machineType] = kept with { TriedUtc = now };
                return kept.Packages is not null && now - kept.ReadUtc < UsableFor ? kept.Packages : null;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<LenovoBiosPackage>> ReadAsync(string machineType, CancellationToken ct)
    {
        var client = _client;
        string catalog;
        using (var r = await client.GetAsync(LenovoCatalog.CatalogUrl(machineType, windows11: true), ct).ConfigureAwait(false))
        {
            if (r.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // A type Lenovo has no Windows 11 catalog for (older models on Windows 10).
                using var r10 = await client.GetAsync(LenovoCatalog.CatalogUrl(machineType, windows11: false), ct).ConfigureAwait(false);
                r10.EnsureSuccessStatusCode();
                catalog = await r10.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            else
            {
                r.EnsureSuccessStatusCode();
                catalog = await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
        }
        var packages = new List<LenovoBiosPackage>();
        foreach (var url in LenovoCatalog.BiosLocations(catalog))
            packages.Add(LenovoBiosPackage.Parse(await client.GetStringAsync(url, ct).ConfigureAwait(false), url));
        return packages;
    }
}
