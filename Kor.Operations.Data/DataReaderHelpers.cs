#nullable enable
using System;
using System.Data;
using System.Globalization;

namespace Kor.Operations.Data;

public static class DataReaderHelpers
{
    public static string GetTrimmed(IDataRecord r, int i)
    {
        if (r.IsDBNull(i)) return "";
        var v = Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? "";
        return v.Trim();
    }

    public static double GetDouble(IDataRecord r, int i)
    {
        if (r.IsDBNull(i)) return 0.0;
        var v = r.GetValue(i);
        if (v is double d) return d;
        if (v is float f) return f;
        if (v is decimal m) return (double)m;
        if (v is long l) return l;
        if (v is int n) return n;
        if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return 0.0;
    }
}
