#nullable enable
using System;
using System.Configuration;
using System.IO;
using System.Text.Json;

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

        public static OdbcSettings Load()
        {
            // 1) Defaults from App.config
            var dsn = ConfigurationManager.AppSettings["Vp.Dsn"];
            var user = ConfigurationManager.AppSettings["Vp.User"];
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
            catch { /* ignore; fall back to empty */ }

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
