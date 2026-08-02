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

    public static long GetInt64OrDefault(this DbDataReader r, int i, long defaultValue = 0)
        => r.IsDBNull(i) ? defaultValue : r.GetInt64(i);

    public static DateTime? GetDateTimeOrNull(this DbDataReader r, int i)
        => r.IsDBNull(i) ? null : r.GetDateTime(i);
}
