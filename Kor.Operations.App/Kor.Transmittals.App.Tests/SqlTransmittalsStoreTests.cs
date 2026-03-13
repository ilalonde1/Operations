#nullable enable
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Kor.Operations.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class SqlTransmittalsStoreTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"transmittals-tests-{Guid.NewGuid():N}.db");
    private SqliteConnection? _masterConnection;

    public async Task InitializeAsync()
    {
        _masterConnection = new SqliteConnection("Data Source=:memory:");
        await _masterConnection.OpenAsync();
        await _masterConnection.ExecuteNonQueryAsync($"ATTACH DATABASE '{_databasePath}' AS dbo;");
        await _masterConnection.ExecuteNonQueryAsync(
            """
            CREATE TABLE dbo.Transmittals (
                Id TEXT PRIMARY KEY,
                ProjectNo TEXT NULL,
                Subject TEXT NULL,
                DriveId TEXT NULL,
                ItemId TEXT NULL,
                SharePointUrl TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CreatedBy TEXT NULL,
                AppVersion TEXT NULL,
                Type TEXT NULL,
                SentAt TEXT NULL,
                SentBy TEXT NULL
            );

            CREATE TABLE dbo.TransmittalRecipients (
                Id TEXT PRIMARY KEY,
                TransmittalId TEXT NOT NULL,
                Email TEXT NOT NULL,
                Kind TEXT NOT NULL,
                LinkId TEXT NOT NULL,
                PersonalShareLink TEXT NULL,
                LastActivityAt TEXT NULL
            );
            """);
    }

    public async Task DisposeAsync()
    {
        if (_masterConnection is not null)
        {
            await _masterConnection.DisposeAsync();
        }

        if (File.Exists(_databasePath))
        {
            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[SqlTransmittalsStoreTests] Temp database cleanup failed: {ex.Message}");
            }
        }
    }

    [Fact]
    public async Task LogTransmittalAsync_InsertsRowRetrievableById()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();
        var createdUtc = DateTime.UtcNow;

        await store.LogTransmittalAsync(
            id,
            "24001",
            "Issue for review",
            "drive-1",
            "item-1",
            "https://sharepoint.example/file",
            createdUtc,
            "ian@example.com",
            "1.2.3");

        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProjectNo, Subject, CreatedBy FROM dbo.Transmittals WHERE Id = @Id;";
        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("24001", reader.GetString(0));
        Assert.Equal("Issue for review", reader.GetString(1));
        Assert.Equal("ian@example.com", reader.GetString(2));
    }

    [Fact]
    public async Task MarkSentAsync_UpdatesSentAtTimestamp()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();
        var sentAt = new DateTime(2026, 3, 12, 18, 0, 0, DateTimeKind.Utc);

        await store.LogTransmittalAsync(
            id,
            "24001",
            "Issue for review",
            "drive-1",
            "item-1",
            "https://sharepoint.example/file",
            DateTime.UtcNow,
            "ian@example.com",
            "1.2.3");

        await store.MarkSentAsync(id, sentAt, "ian@example.com", "1.2.3");

        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SentAt, SentBy FROM dbo.Transmittals WHERE Id = @Id;";
        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var storedAt = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        Assert.Equal(sentAt, DateTime.SpecifyKind(storedAt, DateTimeKind.Utc));
        Assert.Equal("ian@example.com", reader.GetString(1));
    }

    private SqlTransmittalsStore CreateStore()
    {
        var constructor = typeof(SqlTransmittalsStore).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(Func<CancellationToken, Task<DbConnection>>)],
            modifiers: null)
            ?? throw new InvalidOperationException("Internal SQLite test constructor was not found.");

        return (SqlTransmittalsStore)constructor.Invoke(
        [
            new Func<CancellationToken, Task<DbConnection>>(async ct => await OpenConnectionAsync(ct))
        ]);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await connection.ExecuteNonQueryAsync($"ATTACH DATABASE '{_databasePath}' AS dbo;", ct);
        return connection;
    }
}

internal static class SqliteCommandExtensions
{
    public static async Task ExecuteNonQueryAsync(this SqliteConnection connection, string sql, CancellationToken ct = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
