using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace Kor.Operations.Framework;

/// <summary>
/// The two KorStandards logins the Knowledge IDE uses, each from the environment variable that already holds it:
/// <list type="bullet">
/// <item><see cref="Login.Brain"/> - korbrain_app (KORBRAIN_SQL): brain.* (memory, events, module state).</item>
/// <item><see cref="Login.Board"/> - opportunities_app (KOR_ENGINEERINGTOOLS_STANDARDSDB): oversight.* (board, reviews, mistakes).</item>
/// </list>
/// A variable is read from the process, then from the user's registry environment (a session started before it was
/// set does not have it in its process block). The password never reaches a command line or an exception message.
/// </summary>
public static class KorStandardsConnection
{
    public enum Login { Brain, Board }

    public static string VariableFor(Login login) => login switch
    {
        Login.Brain => "KORBRAIN_SQL",
        Login.Board => "KOR_ENGINEERINGTOOLS_STANDARDSDB",
        _ => throw new ArgumentOutOfRangeException(nameof(login)),
    };

    /// <summary>The raw value of the variable, or null when it is set nowhere.</summary>
    public static string? ReadVariable(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(v)) return v;
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        return key?.GetValue(name) as string;
    }

    /// <summary>
    /// Parses "Server=..;Database=..;User Id=..;Password=..;" into a builder. Tolerates the opportunities connection
    /// string's quirk (the password key arrives with an EMPTY name - Operations tools/SqlQuery/oq.py, KOR.Drafter
    /// tools/standards_db.py). Forces a 15 s connect timeout so a VPN stall is an error, never a hang.
    /// </summary>
    public static SqlConnectionStringBuilder Parse(string raw)
    {
        string? server = null, database = null, user = null, password = null, pending = null;
        foreach (var segment in raw.Split(';'))
        {
            var eq = segment.IndexOf('=');
            if (eq < 0) continue;
            var key = segment[..eq].Trim().ToLowerInvariant();
            var value = segment[(eq + 1)..];
            switch (key)
            {
                case "": pending = value; break;
                case "server": case "data source": server = value; break;
                case "database": case "initial catalog": database = value; break;
                case "user id": case "uid": user = value; break;
                case "password": case "pwd": password = value; break;
            }
        }
        password ??= pending;
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(user)
            || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("the connection string is missing server, database, user id or password");
        return new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = database, UserID = user, Password = password,
            TrustServerCertificate = true, ConnectTimeout = 15, ApplicationName = "kor.exe",
        };
    }

    /// <summary>An open connection for the login, or an exception that names the variable (never the password).</summary>
    public static SqlConnection Open(Login login)
    {
        var name = VariableFor(login);
        var raw = ReadVariable(name) ?? throw new InvalidOperationException($"{name} is not set");
        var builder = Parse(raw);
        builder.InitialCatalog = "KorStandards";
        var connection = new SqlConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }
}
