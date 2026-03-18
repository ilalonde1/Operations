#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Odbc;
using System.Linq;
using System.Security.Principal;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Services
{
    internal static class SecretMigrationRunner
    {
        private const string DbUserEnv = "KOR_DB_USER";
        private const string DbPasswordEnv = "KOR_DB_PASSWORD";
        private const string OdbcUserEnv = "KOR_ODBC_USER";
        private const string OdbcPasswordEnv = "KOR_ODBC_PASSWORD";

        public static void RunOnceAtStartup()
        {
            if (!IsAdministrator())
            {
                return;
            }

            try
            {
                var secrets = ReadSecretsFromConfig();

                var statuses = new List<MigrationStatus>
                {
                    TrySetMachineEnvIfMissing(DbUserEnv, secrets.DbUser),
                    TrySetMachineEnvIfMissing(DbPasswordEnv, secrets.DbPassword),
                    TrySetMachineEnvIfMissing(OdbcUserEnv, secrets.OdbcUser),
                    TrySetMachineEnvIfMissing(OdbcPasswordEnv, secrets.OdbcPassword)
                };

                var provided = statuses.Where(s => s != MigrationStatus.EmptyValue).ToList();
                bool anySet = provided.Any(s => s == MigrationStatus.Set);
                bool allProvidedAlreadySet = provided.Count > 0 && provided.All(s => s == MigrationStatus.AlreadySet);

                if (anySet || allProvidedAlreadySet)
                {
                    SanitizeConfigSecrets();
                }
            }
            catch
            {
                // Best effort only; app can continue with existing fallback behavior.
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                if (identity == null) return false;
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static (string DbUser, string DbPassword, string OdbcUser, string OdbcPassword) ReadSecretsFromConfig()
        {
            string dbUser = string.Empty;
            string dbPassword = string.Empty;

            ReadSqlCredentials("KorTransmittalsDb", ref dbUser, ref dbPassword);
            ReadSqlCredentials("KorEmailIndex", ref dbUser, ref dbPassword);

            string odbcUser = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.VpUser] ?? string.Empty;
            string odbcPassword = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.VpPassword] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(odbcUser) || string.IsNullOrWhiteSpace(odbcPassword))
            {
                var dsnRaw = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.DeltekOdbcDsn];
                if (!string.IsNullOrWhiteSpace(dsnRaw))
                {
                    try
                    {
                        var dsn = new OdbcConnectionStringBuilder(dsnRaw);
                        if (string.IsNullOrWhiteSpace(odbcUser) && dsn.ContainsKey("UID"))
                            odbcUser = Convert.ToString(dsn["UID"]) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(odbcPassword) && dsn.ContainsKey("PWD"))
                            odbcPassword = Convert.ToString(dsn["PWD"]) ?? string.Empty;
                    }
                    catch
                    {
                        // ignore malformed ODBC connection-string content
                    }
                }
            }

            return (dbUser, dbPassword, odbcUser, odbcPassword);
        }

        private static void ReadSqlCredentials(string connectionName, ref string dbUser, ref string dbPassword)
        {
            var cs = ConfigurationManager.ConnectionStrings[connectionName]?.ConnectionString;
            if (string.IsNullOrWhiteSpace(cs)) return;

            try
            {
                var builder = new SqlConnectionStringBuilder(cs);
                if (string.IsNullOrWhiteSpace(dbUser) && !string.IsNullOrWhiteSpace(builder.UserID))
                    dbUser = builder.UserID;
                if (string.IsNullOrWhiteSpace(dbPassword) && !string.IsNullOrWhiteSpace(builder.Password))
                    dbPassword = builder.Password;
            }
            catch
            {
                // ignore malformed SQL connection-string content
            }
        }

        private static MigrationStatus TrySetMachineEnvIfMissing(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return MigrationStatus.EmptyValue;

            var existing = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(existing))
                return MigrationStatus.AlreadySet;

            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Machine);
            return MigrationStatus.Set;
        }

        private static void SanitizeConfigSecrets()
        {
            try
            {
                var cfg = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                SanitizeSqlConnection(cfg, "KorTransmittalsDb");
                SanitizeSqlConnection(cfg, "KorEmailIndex");

                BlankAppSetting(cfg, "Vp.User");
                BlankAppSetting(cfg, "Vp.Password");

                foreach (string key in cfg.AppSettings.Settings.AllKeys)
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        key.IndexOf("pwd", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        BlankAppSetting(cfg, key);
                    }
                }

                if (cfg.AppSettings.Settings["DeltekOdbcDsn"] != null)
                {
                    var raw = cfg.AppSettings.Settings["DeltekOdbcDsn"].Value ?? string.Empty;
                    try
                    {
                        var dsn = new OdbcConnectionStringBuilder(raw);
                        if (dsn.ContainsKey("UID")) dsn["UID"] = string.Empty;
                        if (dsn.ContainsKey("PWD")) dsn["PWD"] = string.Empty;
                        cfg.AppSettings.Settings["DeltekOdbcDsn"].Value = dsn.ConnectionString;
                    }
                    catch
                    {
                        cfg.AppSettings.Settings["DeltekOdbcDsn"].Value = string.Empty;
                    }
                }

                cfg.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
                ConfigurationManager.RefreshSection("connectionStrings");
            }
            catch
            {
                // best effort
            }
        }

        private static void SanitizeSqlConnection(Configuration cfg, string name)
        {
            var setting = cfg.ConnectionStrings.ConnectionStrings[name];
            if (setting == null) return;

            var raw = setting.ConnectionString;
            if (string.IsNullOrWhiteSpace(raw)) return;

            try
            {
                var builder = new SqlConnectionStringBuilder(raw)
                {
                    UserID = string.Empty,
                    Password = string.Empty
                };
                setting.ConnectionString = builder.ConnectionString;
            }
            catch
            {
                // ignore malformed connection string
            }
        }

        private static void BlankAppSetting(Configuration cfg, string key)
        {
            if (cfg.AppSettings.Settings[key] != null)
                cfg.AppSettings.Settings[key].Value = string.Empty;
        }

        private enum MigrationStatus
        {
            EmptyValue,
            AlreadySet,
            Set,
        }
    }
}
