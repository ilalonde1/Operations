using Microsoft.Data.SqlClient;

namespace Kor.Operations.Framework;

/// <summary>Proves each login reaches KorStandards and can read what it owns. Read-only.</summary>
public static class Ping
{
    public sealed record Result(string Login, bool Ok, string Detail);

    public static IReadOnlyList<Result> Run()
    {
        return new[]
        {
            Probe(KorStandardsConnection.Login.Brain, "SELECT COUNT(*) FROM brain.vw_Memory"),
            Probe(KorStandardsConnection.Login.Board, "SELECT COUNT(*) FROM oversight.vw_Board"),
        };
    }

    private static Result Probe(KorStandardsConnection.Login login, string sql)
    {
        var name = KorStandardsConnection.VariableFor(login);
        try
        {
            using var connection = KorStandardsConnection.Open(login);
            using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
            var rows = Convert.ToInt32(command.ExecuteScalar());
            return new Result(name, true, $"{rows} rows");
        }
        catch (Exception e)
        {
            return new Result(name, false, e.Message);
        }
    }
}
