#nullable enable
using System;
using System.IO;
using System.Text.Json;
using Kor.Operations.App.Options;

namespace Kor.Operations.Services
{
    internal sealed class OdbcSettings
    {
        public string? Dsn { get; init; }
        public string? User { get; init; }
        public string? Pwd { get; init; } // decrypted

        private const string Folder = @"%PROGRAMDATA%\Kor.Transmittals";
        private const string FileName = "odbc.json";

        private sealed class SecretFile { public string? EncPwd { get; set; } }

        public static OdbcSettings Load(DeltekOdbcOptions options)
        {
            // 1) Defaults from App.config
            var dsn = options.Dsn;
            var user = options.User;
            string? pwd = null;

            // 2) Try json in ProgramData for encrypted password
            try
            {
                var dir = Environment.ExpandEnvironmentVariables(Folder);
                var path = Path.Combine(dir, FileName);
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var secret = JsonSerializer.Deserialize<SecretFile>(json);
                    if (!string.IsNullOrWhiteSpace(secret?.EncPwd))
                        pwd = Dpapi.UnprotectFromMachine(secret!.EncPwd!);
                }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to load ODBC secret; falling back to empty credentials."); }

            return new OdbcSettings { Dsn = dsn, User = user, Pwd = pwd };
        }

        public static void SaveEncryptedPassword(string plaintext)
        {
            var enc = Dpapi.ProtectToMachine(plaintext ?? "");
            var dir = Environment.ExpandEnvironmentVariables(Folder);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, FileName);
            var json = JsonSerializer.Serialize(new SecretFile { EncPwd = enc }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}
