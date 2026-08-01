#nullable enable
#pragma warning disable SA1649

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Data
{
    public sealed record TakeoffSnapshotRecord(
        int Id,
        string ProjectWbs1,
        string IssueLabel,
        DateTime IssueDateUtc,
        string? BasisJson,
        string? CoverageJson,
        DateTime CreatedUtc,
        string? CreatedBy,
        string? Notes);

    public sealed record TakeoffLineRecord(
        int Id,
        int SnapshotId,
        string Level,
        string ElementType,
        string? GradeCode,
        double ConcreteM3,
        double FormworkM2,
        double RebarKg,
        string RebarSource,
        string Confidence,
        bool Unresolved,
        string? SourcePdf,
        int? SheetPage,
        string? Note);

    public sealed class SqlTakeoffSnapshotStore
    {
        private readonly string _cs;

        public SqlTakeoffSnapshotStore(string? connectionString = null)
        {
            _cs = connectionString ?? ResolveTransmittalsConnectionString();
        }

        private static string ResolveTransmittalsConnectionString()
        {
            var cs =
                ConfigurationManager.ConnectionStrings[Kor.Operations.Services.AppConfigKeys.ConnectionStrings.KorTransmittalsDb]?.ConnectionString ??
                ConfigurationManager.ConnectionStrings[Kor.Operations.Services.AppConfigKeys.ConnectionStrings.KorTransmittals]?.ConnectionString;

            if (!string.IsNullOrWhiteSpace(cs))
                return cs;

            foreach (ConnectionStringSettings s in ConfigurationManager.ConnectionStrings)
            {
                if (!string.IsNullOrWhiteSpace(s.ConnectionString))
                    return s.ConnectionString;
            }

            return "";
        }

        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
IF OBJECT_ID('dbo.TakeoffSnapshot', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TakeoffSnapshot
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TakeoffSnapshot PRIMARY KEY,
        ProjectWbs1 NVARCHAR(50) NOT NULL,
        IssueLabel NVARCHAR(100) NOT NULL,
        IssueDateUtc DATETIME2 NOT NULL,
        BasisJson NVARCHAR(MAX) NULL,
        CoverageJson NVARCHAR(MAX) NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_TakeoffSnapshot_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy NVARCHAR(128) NULL,
        Notes NVARCHAR(1000) NULL
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_TakeoffSnapshot_Project'
      AND object_id = OBJECT_ID('dbo.TakeoffSnapshot')
)
BEGIN
    CREATE INDEX IX_TakeoffSnapshot_Project
        ON dbo.TakeoffSnapshot(ProjectWbs1, IssueDateUtc DESC);
END;

IF OBJECT_ID('dbo.TakeoffLine', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TakeoffLine
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TakeoffLine PRIMARY KEY,
        SnapshotId INT NOT NULL,
        Level NVARCHAR(50) NOT NULL,
        ElementType NVARCHAR(20) NOT NULL,
        GradeCode NVARCHAR(20) NULL,
        ConcreteM3 FLOAT NOT NULL,
        FormworkM2 FLOAT NOT NULL,
        RebarKg FLOAT NOT NULL,
        RebarSource NVARCHAR(20) NOT NULL,
        Confidence NVARCHAR(20) NOT NULL,
        Unresolved BIT NOT NULL,
        SourcePdf NVARCHAR(260) NULL,
        SheetPage INT NULL,
        Note NVARCHAR(500) NULL,
        CONSTRAINT FK_TakeoffLine_TakeoffSnapshot
            FOREIGN KEY (SnapshotId) REFERENCES dbo.TakeoffSnapshot(Id) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_TakeoffLine_Snapshot'
      AND object_id = OBJECT_ID('dbo.TakeoffLine')
)
BEGIN
    CREATE INDEX IX_TakeoffLine_Snapshot
        ON dbo.TakeoffLine(SnapshotId);
END;";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        public async Task<int> InsertSnapshotAsync(
            TakeoffSnapshotRecord header,
            IReadOnlyList<TakeoffLineRecord> lines,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(header);
            ArgumentNullException.ThrowIfNull(lines);

            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(innerCt);

                try
                {
                    const string headerSql = @"
INSERT INTO dbo.TakeoffSnapshot
    (ProjectWbs1, IssueLabel, IssueDateUtc, BasisJson, CoverageJson, CreatedBy, Notes)
VALUES
    (@ProjectWbs1, @IssueLabel, @IssueDateUtc, @BasisJson, @CoverageJson, @CreatedBy, @Notes);

SELECT CONVERT(INT, SCOPE_IDENTITY());";

                    await using var headerCmd = new SqlCommand(headerSql, cn, tx) { CommandTimeout = SqlTimeouts.Batch };
                    AddNVarChar(headerCmd, "@ProjectWbs1", header.ProjectWbs1, 50);
                    AddNVarChar(headerCmd, "@IssueLabel", header.IssueLabel, 100);
                    AddDateTime2(headerCmd, "@IssueDateUtc", header.IssueDateUtc);
                    AddNVarChar(headerCmd, "@BasisJson", header.BasisJson, -1);
                    AddNVarChar(headerCmd, "@CoverageJson", header.CoverageJson, -1);
                    AddNVarChar(headerCmd, "@CreatedBy", header.CreatedBy, 128);
                    AddNVarChar(headerCmd, "@Notes", header.Notes, 1000);

                    var snapshotId = Convert.ToInt32(await headerCmd.ExecuteScalarAsync(innerCt));

                    const string lineSql = @"
INSERT INTO dbo.TakeoffLine
    (SnapshotId, Level, ElementType, GradeCode, ConcreteM3, FormworkM2, RebarKg,
     RebarSource, Confidence, Unresolved, SourcePdf, SheetPage, Note)
VALUES
    (@SnapshotId, @Level, @ElementType, @GradeCode, @ConcreteM3, @FormworkM2, @RebarKg,
     @RebarSource, @Confidence, @Unresolved, @SourcePdf, @SheetPage, @Note);";

                    foreach (var line in lines)
                    {
                        await using var lineCmd = new SqlCommand(lineSql, cn, tx) { CommandTimeout = SqlTimeouts.Batch };
                        AddInt(lineCmd, "@SnapshotId", snapshotId);
                        AddNVarChar(lineCmd, "@Level", line.Level, 50);
                        AddNVarChar(lineCmd, "@ElementType", line.ElementType, 20);
                        AddNVarChar(lineCmd, "@GradeCode", line.GradeCode, 20);
                        AddFloat(lineCmd, "@ConcreteM3", line.ConcreteM3);
                        AddFloat(lineCmd, "@FormworkM2", line.FormworkM2);
                        AddFloat(lineCmd, "@RebarKg", line.RebarKg);
                        AddNVarChar(lineCmd, "@RebarSource", line.RebarSource, 20);
                        AddNVarChar(lineCmd, "@Confidence", line.Confidence, 20);
                        AddBit(lineCmd, "@Unresolved", line.Unresolved);
                        AddNVarChar(lineCmd, "@SourcePdf", line.SourcePdf, 260);
                        AddNullableInt(lineCmd, "@SheetPage", line.SheetPage);
                        AddNVarChar(lineCmd, "@Note", line.Note, 500);
                        await lineCmd.ExecuteNonQueryAsync(innerCt);
                    }

                    await tx.CommitAsync(innerCt);
                    return snapshotId;
                }
                catch
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }, ct);
        }

        public async Task<List<TakeoffSnapshotRecord>> ListSnapshotsByProjectAsync(
            string projectWbs1,
            CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
SELECT Id, ProjectWbs1, IssueLabel, IssueDateUtc, BasisJson, CoverageJson, CreatedUtc, CreatedBy, Notes
FROM dbo.TakeoffSnapshot
WHERE ProjectWbs1 = @ProjectWbs1
ORDER BY IssueDateUtc DESC, Id DESC;";

                var snapshots = new List<TakeoffSnapshotRecord>();
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                AddNVarChar(cmd, "@ProjectWbs1", projectWbs1, 50);

                await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await r.ReadAsync(innerCt))
                    snapshots.Add(ReadSnapshot(r));

                return snapshots;
            }, ct);
        }

        public async Task<(TakeoffSnapshotRecord? Header, List<TakeoffLineRecord> Lines)> GetSnapshotAsync(
            int snapshotId,
            CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);

                const string headerSql = @"
SELECT Id, ProjectWbs1, IssueLabel, IssueDateUtc, BasisJson, CoverageJson, CreatedUtc, CreatedBy, Notes
FROM dbo.TakeoffSnapshot
WHERE Id = @Id;";

                TakeoffSnapshotRecord? header = null;
                await using (var headerCmd = new SqlCommand(headerSql, cn) { CommandTimeout = SqlTimeouts.Batch })
                {
                    AddInt(headerCmd, "@Id", snapshotId);
                    await using var r = await headerCmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                    if (await r.ReadAsync(innerCt))
                        header = ReadSnapshot(r);
                }

                if (header is null)
                    return ((TakeoffSnapshotRecord?)null, new List<TakeoffLineRecord>());

                const string lineSql = @"
SELECT Id, SnapshotId, Level, ElementType, GradeCode, ConcreteM3, FormworkM2, RebarKg,
       RebarSource, Confidence, Unresolved, SourcePdf, SheetPage, Note
FROM dbo.TakeoffLine
WHERE SnapshotId = @SnapshotId
ORDER BY Id;";

                var lines = new List<TakeoffLineRecord>();
                await using (var lineCmd = new SqlCommand(lineSql, cn) { CommandTimeout = SqlTimeouts.Batch })
                {
                    AddInt(lineCmd, "@SnapshotId", snapshotId);
                    await using var r = await lineCmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                    while (await r.ReadAsync(innerCt))
                        lines.Add(ReadLine(r));
                }

                return ((TakeoffSnapshotRecord?)header, lines);
            }, ct);
        }

        public async Task DeleteSnapshotAsync(int snapshotId, CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
DELETE FROM dbo.TakeoffSnapshot
WHERE Id = @Id;";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = SqlTimeouts.Batch };
                AddInt(cmd, "@Id", snapshotId);
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        private static TakeoffSnapshotRecord ReadSnapshot(SqlDataReader r)
        {
            return new TakeoffSnapshotRecord(
                r.GetInt32(0),
                r.GetString(1),
                r.GetString(2),
                r.GetDateTime(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetDateTime(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8));
        }

        private static TakeoffLineRecord ReadLine(SqlDataReader r)
        {
            return new TakeoffLineRecord(
                r.GetInt32(0),
                r.GetInt32(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetDouble(5),
                r.GetDouble(6),
                r.GetDouble(7),
                r.GetString(8),
                r.GetString(9),
                r.GetBoolean(10),
                r.IsDBNull(11) ? null : r.GetString(11),
                r.IsDBNull(12) ? null : r.GetInt32(12),
                r.IsDBNull(13) ? null : r.GetString(13));
        }

        private static void AddNVarChar(SqlCommand cmd, string name, string? value, int size)
        {
            cmd.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value });
        }

        private static void AddDateTime2(SqlCommand cmd, string name, DateTime value)
        {
            cmd.Parameters.Add(new SqlParameter(name, SqlDbType.DateTime2) { Value = value });
        }

        private static void AddFloat(SqlCommand cmd, string name, double value)
        {
            cmd.Parameters.Add(new SqlParameter(name, SqlDbType.Float) { Value = value });
        }

        private static void AddInt(SqlCommand cmd, string name, int value)
        {
            cmd.Parameters.Add(new SqlParameter(name, SqlDbType.Int) { Value = value });
        }

        private static void AddNullableInt(SqlCommand cmd, string name, int? value)
        {
            cmd.Parameters.Add(new SqlParameter(name, SqlDbType.Int) { Value = (object?)value ?? DBNull.Value });
        }

        private static void AddBit(SqlCommand cmd, string name, bool value)
        {
            cmd.Parameters.Add(new SqlParameter(name, SqlDbType.Bit) { Value = value });
        }
    }
}
