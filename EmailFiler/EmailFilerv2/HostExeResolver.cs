using System;
using System.Configuration;
using System.IO;
using System.Reflection;

namespace EmailFilerv2
{
    internal static class HostExeResolver
    {
        internal static string Resolve()
        {
            try
            {
                var configured = ConfigurationManager.AppSettings["KorTransmittalsAppPath"];
                if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                    return configured;

                var envPath = Environment.GetEnvironmentVariable("KOR_OPERATIONS_APP_PATH");
                if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
                    return envPath;

                var dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var candidate = Path.Combine(dllDir, "Kor.Operations.App.exe");
                if (File.Exists(candidate))
                    return candidate;

                var prodFallback = @"C:\Newerforma\Kor.Operations.App.exe";
                if (File.Exists(prodFallback))
                    return prodFallback;
            }
            catch
            {
                return null;
            }

            return null;
        }
    }
}
