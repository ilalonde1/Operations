#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        // Options that skip byte[] deserialization — used by LoadAll() so megabytes of
        // embedded images are never allocated just to populate a picker list.
        protected static readonly JsonSerializerOptions JsonOptionsNoImages = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(), new SkipByteArrayConverter() }
        };

        protected SqlJsonStore(string connectionString)
            => _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        protected abstract string TableName { get; }

        // Subclasses can override to use JsonOptionsNoImages for LoadAll()
        protected virtual JsonSerializerOptions LoadAllDeserializeOptions => JsonOptions;

        public List<T> LoadAll()
        {
            var list = new List<T>();
            using var cn = new SqlConnection(_cs);
            cn.Open();
            using var cmd = new SqlCommand(
                $"SELECT ContentJson FROM {TableName} ORDER BY ModifiedAt DESC;",
                cn) { CommandTimeout = SqlTimeouts.UiFacing };
            using var r = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
            while (r.Read())
            {
                var json = r.GetStringOrEmpty(0);
                if (string.IsNullOrWhiteSpace(json)) continue;
                var item = JsonSerializer.Deserialize<T>(json, LoadAllDeserializeOptions);
                if (item is not null) list.Add(item);
            }
            return list;
        }

        public T? Load(string id)
        {
            using var cn = new SqlConnection(_cs);
            cn.Open();
            using var cmd = new SqlCommand(
                $"SELECT ContentJson FROM {TableName} WHERE Id = @Id;", cn)
                { CommandTimeout = SqlTimeouts.UiFacing };
            cmd.Parameters.AddWithValue("@Id", id ?? string.Empty);
            var json = cmd.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(json)) return default;
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }

        public void Delete(string id)
        {
            using var cn = new SqlConnection(_cs);
            cn.Open();
            using var cmd = new SqlCommand($"DELETE FROM {TableName} WHERE Id = @Id;", cn)
                { CommandTimeout = SqlTimeouts.Batch };
            cmd.Parameters.AddWithValue("@Id", id ?? string.Empty);
            cmd.ExecuteNonQuery();
        }
    }
}
