using Kor.Operations.Framework.Enforce;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1212: a SQL write piped into a cut is refused; reads, uncut writes and header-dropping tails pass.
/// Pure: no database, no files. Does NOT cover: Claude Code applying the deny, or the GuardRefusal row (the CLI's, as for
/// row 1935's guard).
/// </summary>
public sealed class SqlWriteCutGuardTests
{
    [Theory]
    [InlineData("Bash", @"sqlcmd -S KOR-APP01\SQLEXPRESS -t 60 -i db/186_x.sql | tail -5")]                                     // mistake 53's shape
    [InlineData("Bash", "sqlcmd -S h -t 30 -Q \"UPDATE oversight.Commitment SET State='done' WHERE Id=1\" | tail -1")]
    [InlineData("Bash", "sqlcmd -S h -t 30 -Q \"DELETE FROM brain.Memory WHERE Id = 9\" 2>&1 | head -3")]                  // 2>&1 stays in the pipe
    [InlineData("Bash", "cd \"C:/x\" && sqlcmd -S h -t 30 -i a.sql 2>&1 | grep -v Changed | tail -2")]                       // through a filter
    [InlineData("PowerShell", "powershell -File db/tools/Invoke-KorSqlScript.ps1 -Path db/186.sql 2>&1 | Select-Object -Last 3")]
    [InlineData("PowerShell", "Invoke-Sqlcmd -ServerInstance h -InputFile x.sql | Select-Object -First 1")]
    [InlineData("PowerShell", "& .\\db\\tools\\Invoke-KorSqlScript.ps1 -Path db/186.sql | Select-Object -Last 2")]
    [InlineData("Bash", "pwsh -NoProfile -File \"C:/x/db/tools/Invoke-KorSqlScript.ps1\" -Path db/186.sql 2>&1 | tail -3")]
    [InlineData("Bash", "\"C:/Program Files/sqlcmd/sqlcmd.exe\" -S h -t 30 -i a.sql | tail -1")]
    [InlineData("Bash", "sqlcmd -S h -t 30 -Q \"MERGE INTO x USING y ON 1=1 WHEN MATCHED THEN DELETE;\" | grep -m 1 rows")]
    public void AWriteReadThroughACutIsRefused(string tool, string command) =>
        Assert.NotNull(SqlWriteCutGuard.Decide(tool, command));

    [Theory]
    [InlineData("Bash", "sqlcmd -S h -t 30 -i db/186.sql")]                                                                    // whole
    [InlineData("Bash", "sqlcmd -S h -t 30 -Q \"SELECT TOP 5 * FROM oversight.Commitment\" | head -5")]                       // a read
    [InlineData("Bash", "sqlcmd -S h -t 30 -i db/186.sql | tail -n +3")]                                                      // drops a header only
    [InlineData("Bash", "grep -n \"INSERT INTO\" db/186.sql | head")]                                                          // a mention
    [InlineData("Bash", "echo \"sqlcmd -i x.sql | tail -1\"")]                                                                // quoted
    [InlineData("Bash", "sqlcmd -S h -t 30 -i a.sql > out.txt && tail -5 out.txt")]                                          // not a pipe (stated miss)
    [InlineData("Bash", "sqlcmd -S h -t 30 -i a.sql || tail -1 log.txt")]                                                     // || is not a pipe
    [InlineData("Write", "sqlcmd -i x.sql | tail -1")]                                                                        // not a shell tool
    // replay 19 Nov: the 7 real commands the first version refused, all mentions of the script, none a run
    [InlineData("Bash", "cd \"/c/VIsual Studio Projects\" && f=$(ls */tools/Invoke-KorSqlScript.ps1 */db/Invoke-KorSqlScript.ps1 2>/dev/null | head -1); echo \"$f\"")]
    [InlineData("Bash", "cd \"/c/x/db/tools\" && grep -nE \"^\\s*#|param\\(|Credential\" Invoke-KorSqlScript.ps1 | head -40")]
    [InlineData("Bash", "\"C:/KOR-Operations/kor/kor.exe\" record --kind finding --says \"Migration 184 applied via Invoke-KorSqlScript.ps1 with sqlcmd -i\" | tail -1")]
    [InlineData("Bash", "P='(?:sqlcmd|Invoke-Sqlcmd) -i x' && kor check-add --pattern \"$P\" 2>&1 | tail -6")]
    public void ReadsWholeWritesAndMentionsPass(string tool, string command) =>
        Assert.Null(SqlWriteCutGuard.Decide(tool, command));

    [Fact]
    public void TheRefusalNamesTheWriteAndTheCut()
    {
        var r = SqlWriteCutGuard.Decide("Bash", "sqlcmd -S h -t 30 -i db/186.sql | tail -5")!;
        var says = SqlWriteCutGuard.Says(r);
        Assert.Contains("db/186.sql", says);
        Assert.Contains("tail -5", says);
        Assert.Contains("row 1212", says);
    }
}
