#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
namespace Kor.Operations.Services
{
    internal static class DeltekHealthProbe
    {
        internal sealed class Status
        {
            public bool IsOnline { get; init; }
            public string? Message { get; init; }
            public DateTime CheckedUtc { get; init; }
        }

        private static readonly SemaphoreSlim _gate = new(1, 1);
        private static volatile Status _cached = new() { IsOnline = true, Message = null, CheckedUtc = DateTime.MinValue };
        private static long _lastCheckedTicksUtc = DateTime.MinValue.Ticks;
        private static readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);

        public static async Task<Status> GetStatusAsync(DeltekOdbcOptions options, bool force = false)
        {
            var now = DateTime.UtcNow;
            var last = new DateTime(Volatile.Read(ref _lastCheckedTicksUtc), DateTimeKind.Utc);
            if (!force && (now - last) < _ttl)
                return _cached;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                now = DateTime.UtcNow;
                last = new DateTime(Volatile.Read(ref _lastCheckedTicksUtc), DateTimeKind.Utc);
                if (!force && (now - last) < _ttl)
                    return _cached;

                var status = await ProbeOnceAsync(options).ConfigureAwait(false);
                _cached = status;
                Volatile.Write(ref _lastCheckedTicksUtc, status.CheckedUtc.Ticks);
                return status;
            }
            finally
            {
                _gate.Release();
            }
        }

        private static Task<Status> ProbeOnceAsync(DeltekOdbcOptions options)
        {
            return Task.Run(() =>
            {
                var checkedUtc = DateTime.UtcNow;
                try
                {
                    // Use the same settings the app uses everywhere else.
                    var dsn = string.IsNullOrWhiteSpace(options.Dsn) ? "Deltek" : options.Dsn;
                    var user = options.User ?? string.Empty;
                    var pwd = options.Password ?? string.Empty;

                    // Try to keep driver waits short. Unknown keys are generally ignored by OdbcConnectionStringBuilder.
                    var factory = new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>
                    {
                        { "Login Timeout", "5" },
                        { "Connection Timeout", "5" }
                    });

                    using var cn = factory.Create();
                    cn.Open();

                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = "SELECT 1";
                    cmd.CommandTimeout = SqlTimeouts.UiFacing;
                    _ = cmd.ExecuteScalar();

                    return new Status
                    {
                        IsOnline = true,
                        Message = null,
                        CheckedUtc = checkedUtc
                    };
                }
                catch (Exception)
                {
                    // Keep message user-facing and short. Full exception details belong in logs.
                    string msg = "Deltek is unavailable (maintenance or network).";
                    return new Status
                    {
                        IsOnline = false,
                        Message = msg,
                        CheckedUtc = checkedUtc
                    };
                }
            });
        }
    }
}
