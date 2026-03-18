#nullable enable
#pragma warning disable SA1649
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
namespace Kor.Operations.Data
{
    public sealed class UserPreferences
    {
        public string UserUpn { get; set; } = string.Empty;
        public bool AutoFileOnSend { get; set; }
        public bool ItemsToFileEnabled { get; set; }
        public string? EmailSignatureHtml { get; set; }
    }

    public interface IUserPreferencesStore
    {
        Task<UserPreferences> GetAsync(string userUpn, CancellationToken ct = default);
        Task SaveAsync(UserPreferences prefs, CancellationToken ct = default);
    }

    public sealed class SqlUserPreferencesStore : IUserPreferencesStore
    {
        private readonly string _connString;

        public SqlUserPreferencesStore(string connString)
        {
            _connString = connString ?? throw new ArgumentNullException(nameof(connString));
        }

        public async Task<UserPreferences> GetAsync(string userUpn, CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
SELECT UserUpn, AutoFileOnSend, ItemsToFileEnabled, EmailSignatureHtml
FROM dbo.UserPreferences
WHERE UserUpn = @UserUpn;";

                await using var cn = new SqlConnection(_connString);
                await cn.OpenAsync(innerCt);

                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                cmd.Parameters.AddWithValue("@UserUpn", userUpn);

                await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, innerCt);

                if (await rd.ReadAsync(innerCt))
                {
                    return new UserPreferences
                    {
                        UserUpn = rd.GetString(0),
                        AutoFileOnSend = rd.GetBoolean(1),
                        ItemsToFileEnabled = rd.GetBoolean(2),
                        EmailSignatureHtml = rd.GetStringOrNull(3)
                    };
                }

                return new UserPreferences
                {
                    UserUpn = userUpn,
                    AutoFileOnSend = false,
                    ItemsToFileEnabled = false,
                    EmailSignatureHtml = null
                };
            }, ct);
        }

        public async Task SaveAsync(UserPreferences prefs, CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
MERGE dbo.UserPreferences AS t
USING (VALUES(@UserUpn, @AutoFileOnSend, @ItemsToFileEnabled, @EmailSignatureHtml)) v(UserUpn, AutoFileOnSend, ItemsToFileEnabled, EmailSignatureHtml)
ON t.UserUpn = v.UserUpn
WHEN MATCHED THEN
    UPDATE SET AutoFileOnSend = v.AutoFileOnSend,
               ItemsToFileEnabled = v.ItemsToFileEnabled,
               EmailSignatureHtml = v.EmailSignatureHtml,
               ModifiedUtc = sysutcdatetime()
WHEN NOT MATCHED THEN
    INSERT (UserUpn, AutoFileOnSend, ItemsToFileEnabled, EmailSignatureHtml)
    VALUES (v.UserUpn, v.AutoFileOnSend, v.ItemsToFileEnabled, v.EmailSignatureHtml);";

                await using var cn = new SqlConnection(_connString);
                await cn.OpenAsync(innerCt);

                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                cmd.Parameters.AddWithValue("@UserUpn", prefs.UserUpn);
                cmd.Parameters.AddWithValue("@AutoFileOnSend", prefs.AutoFileOnSend);
                cmd.Parameters.AddWithValue("@ItemsToFileEnabled", prefs.ItemsToFileEnabled);
                cmd.Parameters.AddWithValue("@EmailSignatureHtml", (object?)prefs.EmailSignatureHtml ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }
    }
}
