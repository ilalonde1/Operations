#nullable enable
using System;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace Kor.Operations.Data;

public static class SqlDataReaderExtensions
{

    public static string GetStringOrEmpty(this DbDataReader r, int i)
        => r.IsDBNull(i) ? string.Empty : r.GetString(i).Trim();

    public static string? GetStringOrNull(this DbDataReader r, int i)
        => r.IsDBNull(i) ? null : r.GetString(i).Trim();

    public static int GetInt32OrDefault(this DbDataReader r, int i, int defaultValue = 0)
        => r.IsDBNull(i) ? defaultValue : r.GetInt32(i);

    public static long GetInt64OrDefault(this DbDataReader r, int i, long defaultValue = 0)
        => r.IsDBNull(i) ? defaultValue : r.GetInt64(i);

    public static decimal GetDecimalOrDefault(this DbDataReader r, int i, decimal defaultValue = 0m)
        => r.IsDBNull(i) ? defaultValue : r.GetDecimal(i);

    public static double GetDoubleOrDefault(this DbDataReader r, int i, double defaultValue = 0d)
        => r.IsDBNull(i) ? defaultValue : r.GetDouble(i);

    public static DateTime? GetDateTimeOrNull(this DbDataReader r, int i)
        => r.IsDBNull(i) ? null : r.GetDateTime(i);

    public static bool GetBooleanOrDefault(this DbDataReader r, int i, bool defaultValue = false)
        => r.IsDBNull(i) ? defaultValue : r.GetBoolean(i);
}
