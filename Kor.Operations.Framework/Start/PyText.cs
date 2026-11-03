using System.Globalization;
using System.Text;

namespace Kor.Operations.Framework.Start;

/// <summary>
/// The few Python string rules board_render.py's text depends on, so the C# port renders the same characters (K6: the
/// board part is byte-for-byte board_render's text for the same rows). Python counts and slices CODE POINTS (a character
/// outside the BMP is 1, where .NET's Length says 2); its <c>str.split()</c> / <c>rstrip()</c> whitespace adds U+001C-U+001F
/// to .NET's <see cref="char.IsWhiteSpace(char)"/>; <c>str(None)</c> is "None"; <c>format(x, "g")</c> is 6 significant
/// digits with trailing zeros dropped and an exponent past 1e6 or below 1e-4.
/// </summary>
public static class PyText
{
    /// <summary>Python's str() of a value that may be missing: null is "None".</summary>
    public static string Str(object? v) => v switch
    {
        null => "None",
        string s => s,
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => Repr(d),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "None",
    };

    /// <summary>Python truthiness of an optional string (None and "" are false).</summary>
    public static bool Truthy(string? s) => !string.IsNullOrEmpty(s);

    /// <summary>len(s) in code points.</summary>
    public static int Len(string s)
    {
        var n = s.Length;
        for (var i = 0; i + 1 < s.Length; i++)
            if (char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1])) { n--; i++; }
        return n;
    }

    /// <summary>s[:n] in code points (n &gt;= 0).</summary>
    public static string Head(string s, int n)
    {
        if (n <= 0) return "";
        var cp = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (cp == n) return s[..i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            cp++;
        }
        return s;
    }

    /// <summary>Python's str.isspace() for one UTF-16 unit (every Python whitespace character is in the BMP).</summary>
    public static bool IsSpace(char c) => char.IsWhiteSpace(c) || c is >= '\u001c' and <= '\u001f';

    public static string RStrip(string s)
    {
        var end = s.Length;
        while (end > 0 && IsSpace(s[end - 1])) end--;
        return s[..end];
    }

    /// <summary><c>" ".join(s.split())</c>.</summary>
    public static string Squash(string s)
    {
        var sb = new StringBuilder(s.Length);
        var word = false;
        foreach (var c in s)
        {
            if (IsSpace(c)) { word = false; continue; }
            if (!word && sb.Length > 0) sb.Append(' ');
            sb.Append(c);
            word = true;
        }
        return sb.ToString();
    }

    /// <summary>Python's <c>format(x, "g")</c>.</summary>
    public static string G(double v)
    {
        if (double.IsNaN(v)) return "nan";
        if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
        if (v == 0) return double.IsNegative(v) ? "-0" : "0";
        var e = v.ToString("E5", CultureInfo.InvariantCulture);   // d.dddddE+ddd, correctly rounded to 6 significant digits
        var mark = e.IndexOf('E');
        var exp = int.Parse(e[(mark + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (exp < -4 || exp >= 6)
        {
            var mant = StripZeros(e[..mark]);
            return $"{mant}e{(exp < 0 ? "-" : "+")}{Math.Abs(exp):00}";
        }
        return StripZeros(v.ToString("F" + (5 - exp).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
    }

    /// <summary>Python's repr() of a float (shortest round-trip, ".0" on a whole number).</summary>
    public static string Repr(double v)
    {
        var r = v.ToString("R", CultureInfo.InvariantCulture);
        return r.Contains('.') || r.Contains('E') || r.Contains('N') || r.Contains('I') ? r : r + ".0";
    }

    private static string StripZeros(string s) => s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
}
