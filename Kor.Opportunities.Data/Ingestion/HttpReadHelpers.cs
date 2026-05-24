#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Data.Ingestion;

internal static class HttpReadHelpers
{
    public static async Task<string> ReadStringWithCapAsync(
        HttpContent content,
        long maxBytes,
        string sourceName,
        CancellationToken ct)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        if (maxBytes <= 0) maxBytes = int.MaxValue;

        if (content.Headers.ContentLength is long known && known > maxBytes)
        {
            throw new InvalidOperationException(
                $"{sourceName} response Content-Length {known} exceeds configured limit ({maxBytes}).");
        }

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            total += n;
            if (total > maxBytes)
            {
                throw new InvalidOperationException(
                    $"{sourceName} response exceeded configured limit while streaming ({total} > {maxBytes}).");
            }
            await ms.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
        }

        var encoding = ResolveEncoding(content) ?? Encoding.UTF8;
        return encoding.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static Encoding? ResolveEncoding(HttpContent c)
    {
        var charset = c.Headers.ContentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset)) return null;
        try { return Encoding.GetEncoding(charset.Trim('"')); }
        catch { return null; }
    }
}
