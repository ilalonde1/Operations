#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.StandardDetails;

internal sealed record StandardDetailsSchemaCheckResult(bool GroupSchemaAvailable, bool MissingSchema, bool PermissionDenied, bool SchemaMismatch);
internal sealed record StandardDetailsGroupRow(long GroupId, long? ParentGroupId, string Name);
internal sealed record StandardDetailsDocumentRow(long DocumentId, string Title, string? DetailNumber, string GroupName, string CurrentOfficialText, byte? LatestStatus);
internal sealed record StandardDetailsVersionRow(long DocumentVersionId, long DocumentId, int VersionNumber, byte Status, bool IsCurrentOfficial, DateTime CreatedUtc, string OriginalFileName, long ContentLengthBytes, string StoragePath, byte[] RowVersion);
internal sealed record StandardDetailsStateChangeResult(int RowsAffected, byte? CurrentStatus, bool EntityMissing);
internal sealed record StandardDetailsDeleteResult(bool EntityMissing, IReadOnlyList<string> DeletedStoragePaths);

internal sealed class StandardDetailsRepository
{
    private const int DocumentTitleMax = 300;
    private const int DocumentDescriptionMax = 2000;
    private const int RowVersionLength = 8;
    private const int FileNameMax = 260;
    private const int FileExtensionMax = 10;
    private const int ContentTypeMax = 127;
    private const int Sha256HexLength = 64;
    private readonly string _connectionString;

    internal StandardDetailsRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    internal async Task<StandardDetailsSchemaCheckResult> EnsureGroupSchemaAsync()
    {
        const string detectSql = @"
SELECT
    CASE WHEN OBJECT_ID('dbo.DocumentGroups', 'U') IS NOT NULL THEN 1 ELSE 0 END AS HasGroups,
    CASE WHEN COL_LENGTH('dbo.Documents', 'DocumentGroupId') IS NOT NULL THEN 1 ELSE 0 END AS HasDocumentGroupId;";

        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var detect = new SqlCommand(detectSql, cn);
            detect.CommandTimeout = SqlTimeouts.UiFacing;
            await using var reader = await detect.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var hasGroups = reader.GetInt32(0) == 1;
                var hasDocGroupId = reader.GetInt32(1) == 1;
                return new StandardDetailsSchemaCheckResult(
                    hasGroups && hasDocGroupId,
                    MissingSchema: !(hasGroups && hasDocGroupId),
                    PermissionDenied: false,
                    SchemaMismatch: false);
            }
        }
        catch (SqlException ex) when (ex.Number is 262 or 229)
        {
            return new StandardDetailsSchemaCheckResult(false, MissingSchema: false, PermissionDenied: true, SchemaMismatch: false);
        }
        catch
        {
            return new StandardDetailsSchemaCheckResult(false, MissingSchema: false, PermissionDenied: false, SchemaMismatch: true);
        }

        return new StandardDetailsSchemaCheckResult(false, MissingSchema: true, PermissionDenied: false, SchemaMismatch: false);
    }

    internal async Task<IReadOnlyList<StandardDetailsGroupRow>> LoadGroupsAsync()
    {
        const string sql = @"
SELECT DocumentGroupId AS GroupId, ParentDocumentGroupId AS ParentGroupId, Name AS GroupName
FROM dbo.DocumentGroups
WHERE IsActive = 1
ORDER BY Name;";

        var items = new List<StandardDetailsGroupRow>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            items.Add(new StandardDetailsGroupRow(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.IsDBNull(2) ? "(Unnamed)" : r.GetString(2)));
        return items;
    }

    internal async Task<IReadOnlyList<StandardDetailsDocumentRow>> LoadDocumentsAsync(bool groupSchemaAvailable, string query, long? effectiveGroupId)
    {
        const string groupedSql = @"
WITH GroupFilter AS
(
    SELECT DocumentGroupId AS GroupId
    FROM dbo.DocumentGroups
    WHERE DocumentGroupId = @groupId
    UNION ALL
    SELECT g.DocumentGroupId AS GroupId
    FROM dbo.DocumentGroups g
    INNER JOIN GroupFilter gf ON g.ParentDocumentGroupId = gf.GroupId
)
SELECT d.DocumentId,
       d.Title,
       d.DetailNumber,
       ISNULL(dg.Name, 'Ungrouped') AS GroupName,
       cur.CurrentOfficialText,
       latest.LatestStatus
FROM dbo.Documents d
LEFT JOIN dbo.DocumentGroups dg ON dg.DocumentGroupId = d.DocumentGroupId
OUTER APPLY
(
    SELECT TOP 1 CONCAT('v', v.VersionNumber) AS CurrentOfficialText
    FROM dbo.DocumentVersions v
    WHERE v.DocumentId = d.DocumentId
      AND v.IsCurrentOfficial = 1
    ORDER BY v.VersionNumber DESC
) cur
OUTER APPLY
(
    SELECT TOP 1 v2.Status AS LatestStatus
    FROM dbo.DocumentVersions v2
    WHERE v2.DocumentId = d.DocumentId
    ORDER BY v2.VersionNumber DESC
) latest
WHERE (@q = '' OR d.Title LIKE @like OR ISNULL(d.Description, '') LIKE @like)
  AND (@groupId IS NULL OR d.DocumentGroupId IN (SELECT GroupId FROM GroupFilter))
ORDER BY d.Title
OPTION (MAXRECURSION 100);";

        const string fallbackSql = @"
SELECT d.DocumentId,
       d.Title,
       d.DetailNumber,
       'Ungrouped' AS GroupName,
       cur.CurrentOfficialText,
       latest.LatestStatus
FROM dbo.Documents d
OUTER APPLY
(
    SELECT TOP 1 CONCAT('v', v.VersionNumber) AS CurrentOfficialText
    FROM dbo.DocumentVersions v
    WHERE v.DocumentId = d.DocumentId
      AND v.IsCurrentOfficial = 1
    ORDER BY v.VersionNumber DESC
) cur
OUTER APPLY
(
    SELECT TOP 1 v2.Status AS LatestStatus
    FROM dbo.DocumentVersions v2
    WHERE v2.DocumentId = d.DocumentId
    ORDER BY v2.VersionNumber DESC
) latest
WHERE (@q = '' OR d.Title LIKE @like OR ISNULL(d.Description, '') LIKE @like)
ORDER BY d.Title;";

        var rows = new List<StandardDetailsDocumentRow>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(groupSchemaAvailable ? groupedSql : fallbackSql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@q", DocumentDescriptionMax, query);
        AddNVarChar(cmd, "@like", DocumentDescriptionMax + 2, $"%{query}%");
        if (groupSchemaAvailable)
            AddParam(cmd, "@groupId", SqlDbType.BigInt, effectiveGroupId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new StandardDetailsDocumentRow(r.GetInt64(0), r.GetStringOrEmpty(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? "Ungrouped" : r.GetString(3), r.IsDBNull(4) ? "None" : r.GetString(4), r.IsDBNull(5) ? null : r.GetByte(5)));
        return rows;
    }

    internal async Task SetDocumentDetailNumberAsync(long documentId, string? detailNumber, Guid actorUserId)
    {
        const string sql = @"
UPDATE dbo.Documents
SET DetailNumber=@detail,
    UpdatedByUserId=@uid,
    UpdatedUtc=SYSUTCDATETIME()
WHERE DocumentId=@id;";

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@detail", 24, detailNumber);
        AddParam(cmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        AddParam(cmd, "@id", SqlDbType.BigInt, documentId);
        await cmd.ExecuteNonQueryAsync();
    }

    internal async Task<IReadOnlyList<StandardDetailsVersionRow>> LoadVersionsAsync(long documentId)
    {
        const string sql = @"
SELECT v.DocumentVersionId, v.DocumentId, v.VersionNumber, v.Status, v.IsCurrentOfficial, v.CreatedUtc,
       b.OriginalFileName, b.ContentLengthBytes, b.StoragePath, v.RowVersion
FROM dbo.DocumentVersions v
INNER JOIN dbo.FileBlobs b ON b.FileBlobId = v.FileBlobId
WHERE v.DocumentId = @docId
ORDER BY v.VersionNumber DESC;";

        var rows = new List<StandardDetailsVersionRow>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(cmd, "@docId", SqlDbType.BigInt, documentId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new StandardDetailsVersionRow(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.GetByte(3), r.GetBoolean(4), r.GetDateTime(5), r.GetStringOrEmpty(6), r.GetInt64OrDefault(7), r.GetStringOrEmpty(8), r.IsDBNull(9) ? Array.Empty<byte>() : (byte[])r[9]));
        return rows;
    }

    internal async Task<int> AddGroupAsync(string? groupName, Guid actorUserId)
    {
        const string sql = @"
INSERT INTO dbo.DocumentGroups (ParentDocumentGroupId, Name, IsActive, CreatedByUserId, CreatedUtc)
        VALUES (NULL, @name, 1, @uid, SYSUTCDATETIME());";
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@name", 200, groupName);
        AddParam(cmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        return await cmd.ExecuteNonQueryAsync();
    }

    internal async Task<int> AddSubgroupAsync(long parentGroupId, string? groupName, Guid actorUserId)
    {
        const string sql = @"
INSERT INTO dbo.DocumentGroups (ParentDocumentGroupId, Name, IsActive, CreatedByUserId, CreatedUtc)
VALUES (@parentId, @name, 1, @uid, SYSUTCDATETIME());";
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(cmd, "@parentId", SqlDbType.BigInt, parentGroupId);
        AddNVarChar(cmd, "@name", 200, groupName);
        AddParam(cmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        return await cmd.ExecuteNonQueryAsync();
    }

    internal async Task<int> RenameGroupAsync(long groupId, string? groupName, Guid actorUserId)
    {
        const string sql = @"
UPDATE dbo.DocumentGroups
SET Name = @name,
    UpdatedByUserId = @uid,
    UpdatedUtc = SYSUTCDATETIME()
WHERE DocumentGroupId = @id;";
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@name", 200, groupName);
        AddParam(cmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        AddParam(cmd, "@id", SqlDbType.BigInt, groupId);
        return await cmd.ExecuteNonQueryAsync();
    }

    internal async Task<int> GetActiveChildGroupCountAsync(long groupId)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.DocumentGroups WHERE ParentDocumentGroupId=@id AND IsActive=1", cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(cmd, "@id", SqlDbType.BigInt, groupId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    internal async Task<int> GetDocumentCountForGroupAsync(long groupId)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Documents WHERE DocumentGroupId=@id", cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(cmd, "@id", SqlDbType.BigInt, groupId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    internal async Task<int> RemoveGroupAsync(long groupId)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("DELETE FROM dbo.DocumentGroups WHERE DocumentGroupId=@id", cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(cmd, "@id", SqlDbType.BigInt, groupId);
        return await cmd.ExecuteNonQueryAsync();
    }

    internal async Task<int> AssignRecordToGroupAsync(long documentId, long? targetGroupId, Guid actorUserId)
    {
        const string sql = @"
UPDATE dbo.Documents
SET DocumentGroupId = @groupId,
    UpdatedByUserId = @uid,
    UpdatedUtc = SYSUTCDATETIME()
WHERE DocumentId = @docId;";
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(cmd, "@groupId", SqlDbType.BigInt, targetGroupId);
        AddParam(cmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        AddParam(cmd, "@docId", SqlDbType.BigInt, documentId);
        return await cmd.ExecuteNonQueryAsync();
    }

    internal async Task<int> CreateDocumentAsync(string? title, string? description, bool groupSchemaAvailable, long? selectedGroupId, Guid actorUserId)
    {
        var sql = groupSchemaAvailable
            ? @"INSERT INTO dbo.Documents (DocumentUid, Title, Description, DocumentGroupId, CreatedByUserId, CreatedUtc)
VALUES (NEWID(), @title, @desc, @groupId, @userId, SYSUTCDATETIME());"
            : @"INSERT INTO dbo.Documents (DocumentUid, Title, Description, CreatedByUserId, CreatedUtc)
VALUES (NEWID(), @title, @desc, @userId, SYSUTCDATETIME());";

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@title", DocumentTitleMax, title);
        AddNVarChar(cmd, "@desc", DocumentDescriptionMax, description);
        if (groupSchemaAvailable)
            AddParam(cmd, "@groupId", SqlDbType.BigInt, selectedGroupId);
        AddParam(cmd, "@userId", SqlDbType.UniqueIdentifier, actorUserId);
        return await cmd.ExecuteNonQueryAsync();
    }

    internal async Task<int> UploadVersionAsync(long documentId, string documentTitle, string sourcePath, string ext, StandardDetailsFileStore fileStore, Guid actorUserId)
    {
        StandardDetailsPreparedFile? preparedFile = null;
        var committed = false;

        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var tx = await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            var lockCmd = new SqlCommand("SELECT 1 FROM dbo.Documents WITH (UPDLOCK, HOLDLOCK) WHERE DocumentId=@docId", cn, (SqlTransaction)tx);
            lockCmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddParam(lockCmd, "@docId", SqlDbType.BigInt, documentId);
            await lockCmd.ExecuteScalarAsync();

            var nextVersionCmd = new SqlCommand("SELECT ISNULL(MAX(VersionNumber),0)+1 FROM dbo.DocumentVersions WITH (UPDLOCK, HOLDLOCK) WHERE DocumentId=@docId", cn, (SqlTransaction)tx);
            nextVersionCmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddParam(nextVersionCmd, "@docId", SqlDbType.BigInt, documentId);
            var nextVersion = Convert.ToInt32(await nextVersionCmd.ExecuteScalarAsync());

            preparedFile = await fileStore.PrepareVersionFileAsync(documentId, documentTitle, nextVersion, sourcePath, ext);

            var insertBlob = new SqlCommand(@"
INSERT INTO dbo.FileBlobs (BlobUid, StoragePath, OriginalFileName, FileExtension, ContentType, ContentLengthBytes, Sha256Hash, UploadedByUserId, CreatedUtc)
VALUES (NEWID(), @path, @name, @ext, @contentType, @len, @sha, @userId, SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS bigint);", cn, (SqlTransaction)tx);
            insertBlob.CommandTimeout = SqlTimeouts.UiFacing;
            AddNVarChar(insertBlob, "@path", 1024, preparedFile.StoragePath);
            AddNVarChar(insertBlob, "@name", FileNameMax, preparedFile.OriginalFileName);
            AddNVarChar(insertBlob, "@ext", FileExtensionMax, preparedFile.FileExtension);
            AddNVarChar(insertBlob, "@contentType", ContentTypeMax, preparedFile.ContentType);
            AddParam(insertBlob, "@len", SqlDbType.BigInt, preparedFile.ContentLengthBytes);
            AddNVarChar(insertBlob, "@sha", Sha256HexLength, preparedFile.Sha256Hash);
            AddParam(insertBlob, "@userId", SqlDbType.UniqueIdentifier, actorUserId);
            var blobId = (long)(await insertBlob.ExecuteScalarAsync() ?? 0L);

            var insertVersion = new SqlCommand(@"
INSERT INTO dbo.DocumentVersions (VersionUid, DocumentId, VersionNumber, FileBlobId, Status, IsCurrentOfficial, CreatedByUserId, CreatedUtc)
VALUES (NEWID(), @docId, @ver, @blobId, 0, 0, @userId, SYSUTCDATETIME());", cn, (SqlTransaction)tx);
            insertVersion.CommandTimeout = SqlTimeouts.UiFacing;
            AddParam(insertVersion, "@docId", SqlDbType.BigInt, documentId);
            AddParam(insertVersion, "@ver", SqlDbType.Int, nextVersion);
            AddParam(insertVersion, "@blobId", SqlDbType.BigInt, blobId);
            AddParam(insertVersion, "@userId", SqlDbType.UniqueIdentifier, actorUserId);
            await insertVersion.ExecuteNonQueryAsync();

            await tx.CommitAsync();
            committed = true;
            return nextVersion;
        }
        catch
        {
            if (!committed)
                fileStore.CleanupPreparedVersionFile(preparedFile);
            throw;
        }
    }

    internal async Task<StandardDetailsDeleteResult> DeleteRecordAsync(long documentId, string documentTitle, Guid actorUserId)
    {
        var deletedPaths = new List<string>();

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        var existsCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Documents WHERE DocumentId=@id", cn, (SqlTransaction)tx);
        existsCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(existsCmd, "@id", SqlDbType.BigInt, documentId);
        var exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync() ?? 0);
        if (exists == 0)
        {
            await tx.RollbackAsync();
            return new StandardDetailsDeleteResult(EntityMissing: true, DeletedStoragePaths: Array.Empty<string>());
        }

        var deleteCmd = new SqlCommand(@"
DECLARE @DocBlobIds TABLE (FileBlobId bigint PRIMARY KEY);

INSERT INTO @DocBlobIds(FileBlobId)
SELECT DISTINCT v.FileBlobId
FROM dbo.DocumentVersions v
WHERE v.DocumentId = @docId;

DELETE ar
FROM dbo.ApprovalRecords ar
INNER JOIN dbo.DocumentVersions v ON v.DocumentVersionId = ar.DocumentVersionId
WHERE v.DocumentId = @docId;

DELETE pr
FROM dbo.PublicationRecords pr
INNER JOIN dbo.DocumentVersions v ON v.DocumentVersionId = pr.DocumentVersionId
WHERE v.DocumentId = @docId;

DELETE FROM dbo.DocumentVersions
WHERE DocumentId = @docId;

DELETE FROM dbo.Documents
WHERE DocumentId = @docId;

DELETE fb
OUTPUT DELETED.StoragePath
FROM dbo.FileBlobs fb
WHERE fb.FileBlobId IN (SELECT b.FileBlobId FROM @DocBlobIds b)
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.DocumentVersions dv
      WHERE dv.FileBlobId = fb.FileBlobId
  );", cn, (SqlTransaction)tx);
        deleteCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(deleteCmd, "@docId", SqlDbType.BigInt, documentId);

        await using (var reader = await deleteCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0) && reader.GetString(0) is { Length: > 0 } p)
                {
                    deletedPaths.Add(p);
                }
            }
        }

        var safeTitle = (documentTitle ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var auditCmd = new SqlCommand(@"
INSERT INTO dbo.AuditEvents (EventUtc, ActorUserId, EntityType, EntityId, EventType, NewValuesJson, Source)
VALUES (SYSUTCDATETIME(), @uid, 'Document', @id, 'Deleted', @json, 'Desktop');", cn, (SqlTransaction)tx);
        auditCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(auditCmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        AddParam(auditCmd, "@id", SqlDbType.BigInt, documentId);
        AddNVarCharMax(auditCmd, "@json", $"{{\"Title\":\"{safeTitle}\"}}");
        await auditCmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        return new StandardDetailsDeleteResult(EntityMissing: false, DeletedStoragePaths: deletedPaths);
    }

    internal async Task<StandardDetailsStateChangeResult> UpdateStatusAsync(long documentVersionId, long documentId, byte[] rowVersion, Guid actorUserId, byte expected, byte next, bool writeAudit)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        var updateCmd = new SqlCommand(@"
UPDATE dbo.DocumentVersions
SET Status=@next, UpdatedByUserId=@uid, UpdatedUtc=SYSUTCDATETIME()
WHERE DocumentVersionId=@id AND Status=@expected AND RowVersion=@rowVersion;", cn);
        updateCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(updateCmd, "@next", SqlDbType.TinyInt, next);
        AddParam(updateCmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        AddParam(updateCmd, "@id", SqlDbType.BigInt, documentVersionId);
        AddParam(updateCmd, "@expected", SqlDbType.TinyInt, expected);
        AddRowVersionParam(updateCmd, "@rowVersion", rowVersion);
        var rows = await updateCmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            var checkCmd = new SqlCommand("SELECT Status FROM dbo.DocumentVersions WHERE DocumentVersionId=@id", cn);
            checkCmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddParam(checkCmd, "@id", SqlDbType.BigInt, documentVersionId);
            var current = await checkCmd.ExecuteScalarAsync();
            return new StandardDetailsStateChangeResult(0, current is null or DBNull ? null : Convert.ToByte(current), current is null or DBNull);
        }

        if (writeAudit)
        {
            var auditCmd = new SqlCommand(@"
INSERT INTO dbo.AuditEvents (EventUtc, ActorUserId, EntityType, EntityId, EventType, NewValuesJson, Source)
VALUES (SYSUTCDATETIME(), @uid, 'DocumentVersion', @id, 'StatusChanged', @json, 'Desktop');", cn);
            auditCmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddParam(auditCmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
            AddParam(auditCmd, "@id", SqlDbType.BigInt, documentVersionId);
            AddNVarCharMax(auditCmd, "@json", $"{{\"Status\":\"{ToStatusText(next)}\"}}");
            await auditCmd.ExecuteNonQueryAsync();
        }

        return new StandardDetailsStateChangeResult(rows, null, false);
    }

    internal async Task<StandardDetailsStateChangeResult> DecideAsync(long documentVersionId, long documentId, byte[] rowVersion, Guid actorUserId, byte targetStatus, int decision)
    {
        var oldStatusText = ToStatusText(1);
        var newStatusText = ToStatusText(targetStatus);
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        var updateCmd = new SqlCommand(@"
UPDATE dbo.DocumentVersions
SET Status=@status, UpdatedByUserId=@uid, UpdatedUtc=SYSUTCDATETIME()
WHERE DocumentVersionId=@id AND Status=@expected AND RowVersion=@rowVersion;", cn, (SqlTransaction)tx);
        updateCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(updateCmd, "@status", SqlDbType.TinyInt, targetStatus);
        AddParam(updateCmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        AddParam(updateCmd, "@id", SqlDbType.BigInt, documentVersionId);
        AddParam(updateCmd, "@expected", SqlDbType.TinyInt, 1);
        AddRowVersionParam(updateCmd, "@rowVersion", rowVersion);
        var rows = await updateCmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            await tx.RollbackAsync();
            var checkCmd = new SqlCommand("SELECT Status FROM dbo.DocumentVersions WHERE DocumentVersionId=@id", cn);
            checkCmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddParam(checkCmd, "@id", SqlDbType.BigInt, documentVersionId);
            var current = await checkCmd.ExecuteScalarAsync();
            return new StandardDetailsStateChangeResult(0, current is null or DBNull ? null : Convert.ToByte(current), current is null or DBNull);
        }

        var approvalCmd = new SqlCommand(@"
INSERT INTO dbo.ApprovalRecords (DocumentVersionId, Decision, Comment, DecidedByUserId, DecidedUtc)
VALUES (@id, @decision, @comment, @uid, SYSUTCDATETIME());", cn, (SqlTransaction)tx);
        approvalCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(approvalCmd, "@id", SqlDbType.BigInt, documentVersionId);
        AddParam(approvalCmd, "@decision", SqlDbType.TinyInt, decision);
        AddNVarChar(approvalCmd, "@comment", DocumentDescriptionMax, decision == 1 ? "Approved from Operations module" : "Rejected from Operations module");
        AddParam(approvalCmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        await approvalCmd.ExecuteNonQueryAsync();

        var auditCmd = new SqlCommand(@"
INSERT INTO dbo.AuditEvents (EventUtc, ActorUserId, EntityType, EntityId, EventType, OldValuesJson, NewValuesJson, Source)
VALUES (SYSUTCDATETIME(), @uid, 'DocumentVersion', @id, 'StatusChanged', @oldJson, @newJson, 'Desktop');", cn, (SqlTransaction)tx);
        auditCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(auditCmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        AddParam(auditCmd, "@id", SqlDbType.BigInt, documentVersionId);
        AddNVarCharMax(auditCmd, "@oldJson", $"{{\"Status\":\"{oldStatusText}\"}}");
        AddNVarCharMax(auditCmd, "@newJson", $"{{\"Status\":\"{newStatusText}\"}}");
        await auditCmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        return new StandardDetailsStateChangeResult(rows, null, false);
    }

    internal async Task<StandardDetailsStateChangeResult> PublishAsync(long documentVersionId, long documentId, byte[] rowVersion, bool oldIsOfficial, Guid actorUserId)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        var checkApprovedCmd = new SqlCommand("SELECT Status FROM dbo.DocumentVersions WHERE DocumentVersionId=@id", cn);
        checkApprovedCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(checkApprovedCmd, "@id", SqlDbType.BigInt, documentVersionId);
        var initialStatus = await checkApprovedCmd.ExecuteScalarAsync();
        if (initialStatus is null or DBNull)
            return new StandardDetailsStateChangeResult(0, null, true);
        if (Convert.ToByte(initialStatus) != 2)
            return new StandardDetailsStateChangeResult(0, Convert.ToByte(initialStatus), false);

        var oldStatusText = ToStatusText(2);
        await using var tx = await cn.BeginTransactionAsync();

        var clearCmd = new SqlCommand("UPDATE dbo.DocumentVersions SET IsCurrentOfficial=0, UpdatedByUserId=@uid, UpdatedUtc=SYSUTCDATETIME() WHERE DocumentId=@docId AND IsCurrentOfficial=1", cn, (SqlTransaction)tx);
        clearCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(clearCmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        AddParam(clearCmd, "@docId", SqlDbType.BigInt, documentId);
        await clearCmd.ExecuteNonQueryAsync();

        var publishCmd = new SqlCommand(@"
UPDATE dbo.DocumentVersions
SET Status=4, IsCurrentOfficial=1, UpdatedByUserId=@uid, UpdatedUtc=SYSUTCDATETIME()
WHERE DocumentVersionId=@id AND Status=@expected AND RowVersion=@rowVersion;", cn, (SqlTransaction)tx);
        publishCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(publishCmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        AddParam(publishCmd, "@id", SqlDbType.BigInt, documentVersionId);
        AddParam(publishCmd, "@expected", SqlDbType.TinyInt, 2);
        AddRowVersionParam(publishCmd, "@rowVersion", rowVersion);
        var publishRows = await publishCmd.ExecuteNonQueryAsync();
        if (publishRows == 0)
        {
            await tx.RollbackAsync();
            var checkCmd = new SqlCommand("SELECT Status FROM dbo.DocumentVersions WHERE DocumentVersionId=@id", cn);
            checkCmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddParam(checkCmd, "@id", SqlDbType.BigInt, documentVersionId);
            var current = await checkCmd.ExecuteScalarAsync();
            return new StandardDetailsStateChangeResult(0, current is null or DBNull ? null : Convert.ToByte(current), current is null or DBNull);
        }

        var recCmd = new SqlCommand(@"
INSERT INTO dbo.PublicationRecords (DocumentVersionId, ActionType, Comment, ActedByUserId, ActedUtc)
VALUES (@id, 1, 'Published from Operations module', @uid, SYSUTCDATETIME());", cn, (SqlTransaction)tx);
        recCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(recCmd, "@id", SqlDbType.BigInt, documentVersionId);
        AddParam(recCmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        await recCmd.ExecuteNonQueryAsync();

        var auditCmd = new SqlCommand(@"
INSERT INTO dbo.AuditEvents (EventUtc, ActorUserId, EntityType, EntityId, EventType, OldValuesJson, NewValuesJson, Source)
VALUES (SYSUTCDATETIME(), @uid, 'DocumentVersion', @id, 'StatusChanged', @oldJson, @newJson, 'Desktop');", cn, (SqlTransaction)tx);
        auditCmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddParam(auditCmd, "@uid", SqlDbType.UniqueIdentifier, actorUserId);
        AddParam(auditCmd, "@id", SqlDbType.BigInt, documentVersionId);
        AddNVarCharMax(auditCmd, "@oldJson", $"{{\"Status\":\"{oldStatusText}\",\"IsCurrentOfficial\":{(oldIsOfficial ? "true" : "false")}}}");
        AddNVarCharMax(auditCmd, "@newJson", "{\"Status\":\"Published\",\"IsCurrentOfficial\":true}");
        await auditCmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        return new StandardDetailsStateChangeResult(publishRows, null, false);
    }

    private static void AddParam(SqlCommand cmd, string name, SqlDbType type, object? value)
    {
        var p = cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static void AddNVarChar(SqlCommand cmd, string name, int size, string? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNVarCharMax(SqlCommand cmd, string name, string? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, -1);
        p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddRowVersionParam(SqlCommand cmd, string name, byte[]? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Binary, RowVersionLength);
        p.Value = value is { Length: RowVersionLength } ? value : DBNull.Value;
    }

    private static string ToStatusText(byte? status)
        => status switch
        {
            0 => "Draft",
            1 => "Submitted",
            2 => "Approved",
            3 => "Rejected",
            4 => "Published",
            5 => "Archived",
            _ => "None"
        };
}
