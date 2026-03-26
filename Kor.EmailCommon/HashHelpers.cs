using System.Security.Cryptography;

namespace Kor.EmailCommon;

public static class HashHelpers
{
    public static string Sha1HexOfFile(string path)
    {
#pragma warning disable CA5350
        using var sha1 = SHA1.Create();
#pragma warning restore CA5350
        using var stream = File.OpenRead(path);
        var hash = sha1.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
    }
}
