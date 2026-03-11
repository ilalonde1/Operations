using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Data.Odbc;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Services
{
    internal static class EnvironmentSecretOverrides
    {
        private const string DbPasswordEnv = "KOR_DB_PASSWORD";
        private const string DbUserEnv = "KOR_DB_USER";
        private const string OdbcPasswordEnv = "KOR_ODBC_PASSWORD";
        private const string OdbcUserEnv = "KOR_ODBC_USER";

        public static void Apply()
        {
            try
            {
                OverrideSqlConnectionString("KorTransmittalsDb");
                OverrideSqlConnectionString("KorEmailIndex");

                OverrideAppSetting("Vp.Password",
                    Environment.GetEnvironmentVariable(OdbcPasswordEnv, EnvironmentVariableTarget.Machine)
                    ?? Environment.GetEnvironmentVariable(OdbcPasswordEnv, EnvironmentVariableTarget.User)
                    ?? Environment.GetEnvironmentVariable(OdbcPasswordEnv));
                OverrideAppSetting("Vp.User",
                    Environment.GetEnvironmentVariable(OdbcUserEnv, EnvironmentVariableTarget.Machine)
                    ?? Environment.GetEnvironmentVariable(OdbcUserEnv, EnvironmentVariableTarget.User)
                    ?? Environment.GetEnvironmentVariable(OdbcUserEnv));

                OverrideOdbcDsnAppSetting("DeltekOdbcDsn");
            }
            catch
            {
                // Never block startup if secret override logic fails.
            }
        }

        private static void OverrideSqlConnectionString(string name)
        {
            var envPassword =
                Environment.GetEnvironmentVariable(DbPasswordEnv, EnvironmentVariableTarget.Machine)
                ?? Environment.GetEnvironmentVariable(DbPasswordEnv, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(DbPasswordEnv);
            var envUser =
                Environment.GetEnvironmentVariable(DbUserEnv, EnvironmentVariableTarget.Machine)
                ?? Environment.GetEnvironmentVariable(DbUserEnv, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(DbUserEnv);

            if (string.IsNullOrWhiteSpace(envPassword) && string.IsNullOrWhiteSpace(envUser))
                return;

            var current = ConfigurationManager.ConnectionStrings[name]?.ConnectionString;
            if (string.IsNullOrWhiteSpace(current))
                return;

            var builder = new SqlConnectionStringBuilder(current);

            if (!string.IsNullOrWhiteSpace(envPassword))
                builder.Password = envPassword;

            if (!string.IsNullOrWhiteSpace(envUser))
                builder.UserID = envUser;

            ForceSetConnectionString(name, builder.ConnectionString);
        }

        private static void ForceSetConnectionString(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var settings = ConfigurationManager.ConnectionStrings[name];
            if (settings == null)
                return;

            // Fast path: works in many environments.
            try
            {
                settings.ConnectionString = value;
                return;
            }
            catch
            {
                // fall through
            }

            // If the config element is marked read-only at runtime, flip it and retry.
            try
            {
                SetConfigurationElementReadOnly(settings, false);
                settings.ConnectionString = value;
                return;
            }
            catch
            {
                // fall through
            }

            // Last resort: set likely backing fields.
            try
            {
                var t = settings.GetType(); // typically ConnectionStringSettings

                var f =
                    t.GetField("_connectionString", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? t.GetField("connectionString", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? t.GetField("ConnectionString", BindingFlags.Instance | BindingFlags.NonPublic);

                if (f != null)
                {
                    f.SetValue(settings, value);
                }
            }
            catch
            {
                // best-effort only
            }
        }

        private static void SetConfigurationElementReadOnly(object element, bool readOnly)
        {
            if (element == null) return;

            var type = typeof(ConfigurationElement);
            var field = type.GetField("_readOnly", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(element, readOnly);
                return;
            }

            var prop = type.GetProperty("IsReadOnly", BindingFlags.Instance | BindingFlags.NonPublic);
            prop?.SetValue(element, readOnly, null);
        }

        private static void OverrideOdbcDsnAppSetting(string key)
        {
            var envPassword =
                Environment.GetEnvironmentVariable(OdbcPasswordEnv, EnvironmentVariableTarget.Machine)
                ?? Environment.GetEnvironmentVariable(OdbcPasswordEnv, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(OdbcPasswordEnv);
            var envUser =
                Environment.GetEnvironmentVariable(OdbcUserEnv, EnvironmentVariableTarget.Machine)
                ?? Environment.GetEnvironmentVariable(OdbcUserEnv, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(OdbcUserEnv);

            if (string.IsNullOrWhiteSpace(envPassword) && string.IsNullOrWhiteSpace(envUser))
                return;

            var current = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(current))
                return;

            var builder = new OdbcConnectionStringBuilder(current);

            if (!string.IsNullOrWhiteSpace(envPassword))
                builder["PWD"] = envPassword;

            if (!string.IsNullOrWhiteSpace(envUser))
                builder["UID"] = envUser;

            OverrideAppSetting(key, builder.ConnectionString);
        }

        private static void OverrideAppSetting(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var appSettings = ConfigurationManager.AppSettings;
            if (appSettings == null)
                return;

            SetReadOnly(appSettings, false);
            appSettings[key] = value;
        }

        private static void SetReadOnly(NameValueCollection collection, bool readOnly)
        {
            var type = typeof(NameObjectCollectionBase);

            var field = type.GetField("_readOnly", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(collection, readOnly);
                return;
            }

            var prop = type.GetProperty("IsReadOnly", BindingFlags.Instance | BindingFlags.NonPublic);
            prop?.SetValue(collection, readOnly, null);
        }
    }
}
