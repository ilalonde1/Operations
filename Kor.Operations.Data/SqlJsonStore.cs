#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Services;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Data
{
    public abstract class SqlJsonStore<T>
        where T : class
    {
        protected readonly string _cs;
        protected static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        protected static readonly JsonSerializerOptions JsonOptionsNoImages = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(), new SkipByteArrayConverter() }
        };

        protected SqlJsonStore(string connectionString)
            => _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        protected abstract string TableName { get; }

        protected virtual JsonSerializerOptions LoadAllDeserializeOptions => JsonOptions;

        public async Task<List<T>> LoadAllAsync(CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                var list = new List<T>();
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt).ConfigureAwait(false);
                await using var cmd = new SqlCommand(
                    $"SELECT ContentJson FROM {TableName} ORDER BY ModifiedAt DESC;",
                    cn) { CommandTimeout = SqlTimeouts.UiFacing };
                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt).ConfigureAwait(false);
                while (await r.ReadAsync(innerCt).ConfigureAwait(false))
                {
                    var json = r.GetStringOrEmpty(0);
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    var item = JsonSerializer.Deserialize<T>(json, LoadAllDeserializeOptions);
                    if (item is not null) list.Add(item);
                }
                return list;
            }, ct).ConfigureAwait(false);
        }

        public async Task<T?> LoadAsync(string id, CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt).ConfigureAwait(false);
                await using var cmd = new SqlCommand(
                    $"SELECT ContentJson FROM {TableName} WHERE Id = @Id;", cn)
                    { CommandTimeout = SqlTimeouts.UiFacing };
                cmd.Parameters.AddWithValue("@Id", id ?? string.Empty);
                var json = (await cmd.ExecuteScalarAsync(innerCt).ConfigureAwait(false)) as string;
                if (string.IsNullOrWhiteSpace(json)) return default;
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }, ct).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt).ConfigureAwait(false);
                await using var cmd = new SqlCommand($"DELETE FROM {TableName} WHERE Id = @Id;", cn)
                    { CommandTimeout = SqlTimeouts.Batch };
                cmd.Parameters.AddWithValue("@Id", id ?? string.Empty);
                await cmd.ExecuteNonQueryAsync(innerCt).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
    }
}
