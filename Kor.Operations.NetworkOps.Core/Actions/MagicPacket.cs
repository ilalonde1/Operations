#nullable enable
using System.Globalization;

namespace Kor.Operations.NetworkOps.Core.Actions;

/// <summary>Wake-on-LAN: the 102-byte magic packet -- six 0xFF, then the target MAC sixteen times.</summary>
public static class MagicPacket
{
    public const string Kind = "wake";

    /// <summary>Parses "18:C0:4D:28:9F:D6" / "18-c0-4d-28-9f-d6" / "18C04D289FD6"; null when it is not a MAC.</summary>
    public static byte[]? ParseMac(string? mac)
    {
        if (mac is null) return null;
        var hex = mac.Replace(":", "").Replace("-", "").Replace(".", "").Trim();
        if (hex.Length != 12) return null;
        var bytes = new byte[6];
        for (var i = 0; i < 6; i++)
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i])) return null;
        return bytes;
    }

    public static byte[] Build(byte[] mac)
    {
        if (mac.Length != 6) throw new ArgumentException("a MAC is 6 bytes", nameof(mac));
        var p = new byte[102];
        for (var i = 0; i < 6; i++) p[i] = 0xFF;
        for (var r = 0; r < 16; r++) Buffer.BlockCopy(mac, 0, p, 6 + r * 6, 6);
        return p;
    }
}
