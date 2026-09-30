#nullable enable
using System.Globalization;
using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace Kor.Operations.NetworkOps.Transport;

/// <summary>SNMPv3 read-only credentials: one user for every device (KOR_NETWORKOPS_SNMP* on APP01).</summary>
/// <param name="AuthSha256">True for SHA-256 (the Eaton card); false for SHA-1 (APC, Synology).</param>
public sealed record SnmpV3Credentials(string User, string AuthPassword, string PrivPassword, bool AuthSha256);

// SNMPv3 GET, authPriv only (never v1/v2c: the cards have them switched off). Values come back as
// decimal strings, TimeTicks as hundredths of a second, and an OID the device does not have is simply
// left out -- the Core parsers decide what a missing value means.
public static class SnmpChannel
{
    public static async Task<IReadOnlyDictionary<string, string>> GetAsync(string host, SnmpV3Credentials creds, IReadOnlyList<string> oids,
        TimeSpan timeout, CancellationToken ct)
    {
        var address = IPAddress.TryParse(host, out var ip) ? ip : (await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false)).First();
        var endpoint = new IPEndPoint(address, 161);
        var ms = (int)timeout.TotalMilliseconds;
        return await Task.Run(() =>
        {
            // The v3 handshake: a discovery report carries the engine id and time the request is keyed to.
            var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
            var report = discovery.GetResponse(ms, endpoint);
            // SHA-1 only where the device offers nothing better: the APC NMC3 and DSM accept SHA or MD5 and
            // nothing else (checked on both, 2026-09-29). HMAC-SHA-1 in the SNMPv3 USM is not what SHA-1's
            // collision attacks break, and every message is AES-encrypted besides. The Eaton gets SHA-256.
#pragma warning disable CS0618
            IAuthenticationProvider auth = creds.AuthSha256
                ? new SHA256AuthenticationProvider(new OctetString(creds.AuthPassword))
                : new SHA1AuthenticationProvider(new OctetString(creds.AuthPassword));
#pragma warning restore CS0618
            var privacy = new AESPrivacyProvider(new OctetString(creds.PrivPassword), auth);
            var request = new GetRequestMessage(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId,
                new OctetString(creds.User), OctetString.Empty, oids.Select(o => new Variable(new ObjectIdentifier(o))).ToList(),
                privacy, Messenger.MaxMessageSize, report);
            var reply = request.GetResponse(ms, endpoint);
            var pdu = reply.Pdu();
            if (pdu.ErrorStatus.ToInt32() != 0)
                throw new InvalidOperationException($"{host} answered SNMP error {pdu.ErrorStatus.ToInt32()} at index {pdu.ErrorIndex.ToInt32()}");

            var values = new Dictionary<string, string>();
            foreach (var v in pdu.Variables)
            {
                var text = v.Data switch
                {
                    NoSuchObject or NoSuchInstance or EndOfMibView => null,
                    TimeTicks t => t.ToUInt32().ToString(CultureInfo.InvariantCulture),
                    Integer32 i => i.ToInt32().ToString(CultureInfo.InvariantCulture),
                    Gauge32 g => g.ToUInt32().ToString(CultureInfo.InvariantCulture),
                    Counter32 c => c.ToUInt32().ToString(CultureInfo.InvariantCulture),
                    _ => v.Data.ToString(),
                };
                if (text is not null) values[v.Id.ToString()] = text;
            }
            return (IReadOnlyDictionary<string, string>)values;
        }, ct).ConfigureAwait(false);
    }
}
