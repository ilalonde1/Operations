#nullable enable
using System.Globalization;
using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace Kor.Operations.NetworkOps.Transport;

/// <summary>SNMPv3 read-only credentials: one user for every device (KOR_NETWORKOPS_SNMP* on APP01).</summary>
/// <param name="AuthSha256">True for SHA-256 (the Eaton card); false for SHA-1 (APC, Synology, the core switch).</param>
/// <param name="PrivDes">True for DES privacy -- the core switch's firmware offers nothing else; everything else is AES.</param>
public sealed record SnmpV3Credentials(string User, string AuthPassword, string PrivPassword, bool AuthSha256, bool PrivDes = false);

// SNMPv3 GET and WALK, authPriv only (never v1/v2c: every rack device has them off) -- except the printers' read-only v1
// GET (GetV1Async, which says why). Values come back as decimal
// strings, TimeTicks as hundredths of a second, and an OID the device does not have is simply left out --
// the Core parsers decide what a missing value means.
public static class SnmpChannel
{
    public static async Task<IReadOnlyDictionary<string, string>> GetAsync(string host, SnmpV3Credentials creds, IReadOnlyList<string> oids,
        TimeSpan timeout, CancellationToken ct)
    {
        var endpoint = await EndpointAsync(host, ct).ConfigureAwait(false);
        var ms = (int)timeout.TotalMilliseconds;
        return await Task.Run(() =>
        {
            // The v3 handshake: a discovery report carries the engine id and time the request is keyed to.
            var report = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu).GetResponse(ms, endpoint);
            var request = new GetRequestMessage(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId,
                new OctetString(creds.User), OctetString.Empty, oids.Select(o => new Variable(new ObjectIdentifier(o))).ToList(),
                Privacy(creds), Messenger.MaxMessageSize, report);
            var pdu = request.GetResponse(ms, endpoint).Pdu();
            if (pdu.ErrorStatus.ToInt32() != 0)
                throw new InvalidOperationException($"{host} answered SNMP error {pdu.ErrorStatus.ToInt32()} at index {pdu.ErrorIndex.ToInt32()}");
            var values = new Dictionary<string, string>();
            foreach (var v in pdu.Variables) if (Text(v.Data) is { } t) values[v.Id.ToString()] = t;
            return (IReadOnlyDictionary<string, string>)values;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Every value under each table OID (GETBULK walk, within the subtree).</summary>
    public static async Task<IReadOnlyDictionary<string, string>> WalkAsync(string host, SnmpV3Credentials creds, IReadOnlyList<string> tables,
        TimeSpan timeout, CancellationToken ct)
    {
        var endpoint = await EndpointAsync(host, ct).ConfigureAwait(false);
        var ms = (int)timeout.TotalMilliseconds;
        return await Task.Run(() =>
        {
            var values = new Dictionary<string, string>();
            foreach (var table in tables)
            {
                var report = Messenger.GetNextDiscovery(SnmpType.GetBulkRequestPdu).GetResponse(ms, endpoint);
                var list = new List<Variable>();
                Messenger.BulkWalk(VersionCode.V3, endpoint, new OctetString(creds.User), OctetString.Empty, new ObjectIdentifier(table), list,
                    ms, 20, WalkMode.WithinSubtree, Privacy(creds), report);
                foreach (var v in list) if (Text(v.Data) is { } t) values[v.Id.ToString()] = t;
            }
            return (IReadOnlyDictionary<string, string>)values;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Every value under each table OID over SNMP v2c with a read-only community (GETBULK walk, within the subtree). The
    /// firewall's exception to v3: the Netgate runs pfSense, whose SNMP service is FreeBSD bsnmpd -- v1/v2c only, no v3
    /// (checked 2026-10-05). A read-only community bound to the LAN, reading standard IF-MIB and HOST-RESOURCES counters,
    /// changes nothing on the firewall. Same shape as WalkAsync, no v3 handshake or privacy.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> WalkV2cAsync(string host, string community, IReadOnlyList<string> tables,
        TimeSpan timeout, CancellationToken ct)
    {
        var endpoint = await EndpointAsync(host, ct).ConfigureAwait(false);
        var ms = (int)timeout.TotalMilliseconds;
        return await Task.Run(() =>
        {
            var community2c = new OctetString(community);
            var values = new Dictionary<string, string>();
            foreach (var table in tables)
            {
                var list = new List<Variable>();
                // v2c GETBULK: no engine discovery, no privacy, no report -- contextName is empty and privacy/report are null.
                Messenger.BulkWalk(VersionCode.V2, endpoint, community2c, OctetString.Empty, new ObjectIdentifier(table), list,
                    ms, 10, WalkMode.WithinSubtree, null, null);
                foreach (var v in list) if (Text(v.Data) is { } t) values[v.Id.ToString()] = t;
            }
            return (IReadOnlyDictionary<string, string>)values;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// SNMP v2c GET of specific scalar OIDs with a read-only community -- the firewall's scalars (sysUpTime, UCD memory,
    /// the PF state counts). v2c, not v1: ifHCInOctets and friends are Counter64, which v1 cannot carry. One batched GET;
    /// an OID the device does not have comes back noSuchObject and is left out (Text returns null).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> GetV2cAsync(string host, string community, IReadOnlyList<string> oids,
        TimeSpan timeout, CancellationToken ct)
    {
        var endpoint = await EndpointAsync(host, ct).ConfigureAwait(false);
        var ms = (int)timeout.TotalMilliseconds;
        return await Task.Run(() =>
        {
            var got = Messenger.Get(VersionCode.V2, endpoint, new OctetString(community),
                oids.Select(o => new Variable(new ObjectIdentifier(o))).ToList(), ms);
            var values = new Dictionary<string, string>();
            foreach (var v in got) if (Text(v.Data) is { } t) values[v.Id.ToString()] = t;
            return (IReadOnlyDictionary<string, string>)values;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// SNMP v1 GET, read-only -- the ONE exception to v3-only above: the printers (2026-10-02). v1, not v2c: the Canons
    /// answer v1 only (v2c timed out on both while v1 -- what Windows' own printer SNMP speaks -- answered). SNMPv3 would need
    /// each printer's admin login to set up, and a read-only "public" GET changes nothing on a printer. An OID the device
    /// does not have is left out. Octet strings named in
    /// <paramref name="hexOids"/> come back as hex: a bit string such as a printer's error flags is not text (0x20 = "low
    /// ink" is the same byte as a space).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> GetV1Async(string host, string community, IReadOnlyList<string> oids,
        IReadOnlyCollection<string> hexOids, TimeSpan timeout, CancellationToken ct)
    {
        var endpoint = await EndpointAsync(host, ct).ConfigureAwait(false);
        var ms = (int)timeout.TotalMilliseconds;
        return await Task.Run(() =>
        {
            // ONE OID per request: the Canons (iR-ADV C5840, TZ-30000) drop -- no answer at all -- a request that names any OID
            // they do not have, so a batch of 10 timed out on both (2026-10-02) while single GETs answered at once. The first
            // OID (sysDescr) must answer, or the device is not answering; after that a missing or silent OID is skipped.
            var values = new Dictionary<string, string>();
            var answered = false;
            foreach (var oid in oids)
            {
                ct.ThrowIfCancellationRequested();
                IList<Variable> got;
                try { got = Messenger.Get(VersionCode.V1, endpoint, new OctetString(community), [new Variable(new ObjectIdentifier(oid))], ms); }
                catch (Exception ex) when (answered && ex is Lextm.SharpSnmpLib.Messaging.TimeoutException or ErrorException) { continue; }
                answered = true;
                foreach (var v in got)
                {
                    var id = v.Id.ToString();
                    var text = v.Data is OctetString os && hexOids.Contains(id) ? os.ToHexString() : Text(v.Data);
                    if (text is not null) values[id] = text;
                }
            }
            return (IReadOnlyDictionary<string, string>)values;
        }, ct).ConfigureAwait(false);
    }

    private static async Task<IPEndPoint> EndpointAsync(string host, CancellationToken ct)
        => new(IPAddress.TryParse(host, out var ip) ? ip : (await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false)).First(), 161);

    // SHA-1 and DES only where the device offers nothing better: the APC NMC3 and DSM accept SHA or MD5 and
    // nothing else, and the core switch's firmware (EdgeSwitch 1.8.1) offers DES as its only privacy (checked on
    // each, 2026-09-29/30). HMAC-SHA-1 in the SNMPv3 USM is not what SHA-1's collision attacks break. The Eaton
    // gets SHA-256, everything but the core switch gets AES.
    private static IPrivacyProvider Privacy(SnmpV3Credentials creds)
    {
#pragma warning disable CS0618
        IAuthenticationProvider auth = creds.AuthSha256
            ? new SHA256AuthenticationProvider(new OctetString(creds.AuthPassword))
            : new SHA1AuthenticationProvider(new OctetString(creds.AuthPassword));
        return creds.PrivDes
            ? new DESPrivacyProvider(new OctetString(creds.PrivPassword), auth)
            : new AESPrivacyProvider(new OctetString(creds.PrivPassword), auth);
#pragma warning restore CS0618
    }

    private static string? Text(ISnmpData data) => data switch
    {
        NoSuchObject or NoSuchInstance or EndOfMibView => null,
        TimeTicks t => t.ToUInt32().ToString(CultureInfo.InvariantCulture),
        Integer32 i => i.ToInt32().ToString(CultureInfo.InvariantCulture),
        Gauge32 g => g.ToUInt32().ToString(CultureInfo.InvariantCulture),
        Counter32 c => c.ToUInt32().ToString(CultureInfo.InvariantCulture),
        Counter64 c => c.ToUInt64().ToString(CultureInfo.InvariantCulture),
        _ => data.ToString(),
    };
}
