#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Kor.Operations.Services
{
    internal static class Dpapi
    {
        public static string ProtectToMachine(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return "";
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var enc = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(enc);
        }

        public static string UnprotectFromMachine(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return "";
            var enc = Convert.FromBase64String(base64);
            var plain = ProtectedData.Unprotect(enc, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
    }
}
