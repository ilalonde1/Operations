#nullable enable
using System.Text;

namespace Kor.Opportunities.Data.Intel;

/// <summary>
/// Text whose UTF-8 bytes were decoded once as Windows-1252: "â€”" where "—" was meant, "Ã©" for "é".
///
/// WHY THIS EXISTS. On 2026-06-14 between 19:00 and 19:09, 436 result files in the honing-orgs queue on
/// KOR-APP01 were rewritten in one pass by a Windows PowerShell 5.1 step (read with no encoding, which is
/// code page 1252 for a file without a byte-order mark; written back with a byte-order mark). The
/// research agents had written them correctly. 491 enrichment payloads and 1,347 intel text values
/// (492 signal subjects repaired by migration 355, 855 more measured the same day) were then ingested
/// garbled, and nothing noticed for four months.
///
/// HOW IT REPAIRS. Every character that Windows-1252 maps to a byte of 0x80 or above is turned back into
/// that byte; wherever those bytes form a complete, valid UTF-8 sequence, the sequence is decoded and
/// replaces the characters. Anything else is left exactly as it was. That makes the repair local and
/// conservative:
///   * a genuine accent is a lone high byte, never a valid sequence, so "Mâmawatoskêwak", "Gagné" and
///     "École" are untouched;
///   * a value holding both a correct "—" and a garbled "â€”" gets only the garbled one fixed;
///   * a character outside Windows-1252 (CJK, a correct emoji) ends the run and is kept.
///
/// WHAT IT DOES NOT CATCH. Text decoded twice ("Ã¢â‚¬â€") is repaired one level only. A misdecode through
/// a code page other than 1252 (Latin-1 drops the 0x80-0x9F specials, so "€" never appears) is not
/// recognised. A deliberate literal "Ã©" in source text would be "repaired" — none exists in the store.
/// </summary>
public static class Utf8Misdecode
{
    private static readonly Encoding Cp1252;
    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static Utf8Misdecode()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    /// <summary>Cheap pre-filter: a garbled UTF-8 lead byte reads as U+00C2..U+00F4 in Windows-1252.</summary>
    public static bool MayBeMisdecoded(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var ch in s)
        {
            if (ch >= 'Â' && ch <= 'ô') return true;
        }

        return false;
    }

    /// <summary>
    /// Repairs every garbled UTF-8 sequence in <paramref name="s"/>. Returns false, and hands back the
    /// input unchanged, when there was nothing to repair.
    /// </summary>
    public static bool TryRepair(string? s, out string repaired)
    {
        repaired = s ?? string.Empty;
        if (s is null || !MayBeMisdecoded(s)) return false;

        var sb = new StringBuilder(s.Length);
        var changed = false;
        var i = 0;
        while (i < s.Length)
        {
            var lead = ByteOf(s[i]);
            var len = lead switch
            {
                >= 0xC2 and <= 0xDF => 2,
                >= 0xE0 and <= 0xEF => 3,
                >= 0xF0 and <= 0xF4 => 4,
                _ => 0,
            };
            if (len > 0 && i + len <= s.Length && TryDecode(s, i, len, out var decoded))
            {
                sb.Append(decoded);
                i += len;
                changed = true;
                continue;
            }

            sb.Append(s[i]);
            i++;
        }

        if (!changed) return false;
        repaired = sb.ToString();
        return true;
    }

    /// <summary>
    /// The inverse: what <paramref name="clean"/> looked like after its UTF-8 bytes were read as
    /// Windows-1252. Used to PROVE a stored natural key was computed from garbled text before re-keying
    /// it. Null when the bytes cannot be read back as Windows-1252, in which case no proof is possible.
    /// </summary>
    public static string? Garble(string clean)
    {
        try
        {
            return Cp1252.GetString(Encoding.UTF8.GetBytes(clean));
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool TryDecode(string s, int start, int len, out string decoded)
    {
        decoded = string.Empty;
        var bytes = new byte[len];
        for (var k = 0; k < len; k++)
        {
            var b = ByteOf(s[start + k]);
            if (b < 0) return false;
            if (k > 0 && (b < 0x80 || b > 0xBF)) return false;
            bytes[k] = (byte)b;
        }

        try
        {
            decoded = Utf8Strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>The Windows-1252 byte for <paramref name="ch"/> when it is 0x80 or above; otherwise -1.</summary>
    private static int ByteOf(char ch)
    {
        if (ch < '\u0080') return -1;
        Span<char> one = stackalloc char[1];
        Span<byte> b = stackalloc byte[2];
        one[0] = ch;
        try
        {
            return Cp1252.GetBytes(one, b) == 1 && b[0] >= 0x80 ? b[0] : -1;
        }
        catch (EncoderFallbackException)
        {
            return -1;
        }
    }
}
