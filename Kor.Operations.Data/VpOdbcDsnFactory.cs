#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Odbc;
namespace Kor.Operations.Data
{
    public interface IOdbcConnectionFactory
    {
        OdbcConnection Create();
    }

    /// <summary>
    /// Creates an ODBC connection for a SYSTEM DSN using explicit UID/PWD.
    /// For the Progress DataDirect Hybrid driver, keep the string minimal:
    /// DSN + UID + PWD (+ optional driver-specific extras).
    /// </summary>
    public sealed class VpOdbcDsnFactory : IOdbcConnectionFactory
    {
        private readonly string _dsn;
        private readonly string? _user;
        private readonly string? _password;
        private readonly Func<Dictionary<string, string>>? _extra;

        public VpOdbcDsnFactory(
            string dsn,
            string? user,
            string? password,
            Func<Dictionary<string, string>>? extra = null)
        {
            _dsn = dsn ?? throw new ArgumentNullException(nameof(dsn));
            _user = user;
            _password = password;
            _extra = extra;
        }

        public OdbcConnection Create()
        {
            var b = new OdbcConnectionStringBuilder
            {
                // ---- Only what the DataDirect Hybrid driver accepts generally ----
                ["DSN"] = _dsn,
                ["UID"] = _user ?? string.Empty,
                ["PWD"] = _password ?? string.Empty
            };

            // Optional: pass strictly driver-supported extras (if needed)
            if (_extra != null)
            {
                foreach (var kv in _extra())
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key))
                        b[kv.Key] = kv.Value ?? string.Empty;
                }
            }

            return new OdbcConnection(b.ConnectionString);
        }
    }
}
