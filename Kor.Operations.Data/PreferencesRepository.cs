#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
namespace Kor.Operations.Data
{
    public sealed class PreferencesRepository
    {
        private readonly string _cs;

        /// <summary>
        /// Construct with an explicit connection string, or use the first non-empty
        /// connection string from App.config (same behavior your app had before).
        /// </summary>
        public PreferencesRepository(string? connectionString = null)
        {
            _cs = connectionString ?? ResolveConnectionString();
            // no extra validation here; match prior behavior
        }

        private static string ResolveConnectionString()
        {
            // EXACTLY: first non-empty connection string in the startup app's config.
            foreach (ConnectionStringSettings s in ConfigurationManager.ConnectionStrings)
            {
                if (!string.IsNullOrWhiteSpace(s.ConnectionString))
                    return s.ConnectionString;
            }
            return ""; // let SqlClient raise the normal connect error if empty
        }

        // ----- Utility -----
        public sealed record DbInfo(string Server, string Database, string Login);
        public async Task<DbInfo> WhoAmIAsync()
        {
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            const string sql = "SELECT @@SERVERNAME, DB_NAME(), SUSER_SNAME();";
            await using var cmd = new SqlCommand(sql, con);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var server = r.IsDBNull(0) ? "" : r.GetString(0);
                var db = r.IsDBNull(1) ? "" : r.GetString(1);
                var login = r.IsDBNull(2) ? "" : r.GetString(2);
                return new DbInfo(server, db, login);
            }
            return new DbInfo("", "", "");
        }

        // ===== Favorites =====
        public async Task<List<(string ProjectNo, string? ProjectName)>> GetFavoritesAsync(string userUpn)
        {
            const string sql = @"
SELECT ProjectNo, ProjectName
FROM dbo.UserFavorites
WHERE UserUpn = @upn
ORDER BY ProjectNo;";
            var list = new List<(string, string?)>();
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var upn = cmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
            upn.Value = userUpn ?? (object)DBNull.Value;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)));
            return list;
        }

        public async Task AddFavoriteAsync(string userUpn, string projectNo, string? projectName)
        {
            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.UserFavorites WHERE UserUpn=@upn AND ProjectNo=@no)
BEGIN
    INSERT INTO dbo.UserFavorites(UserUpn, ProjectNo, ProjectName, CreatedUtc)
    VALUES(@upn, @no, @name, SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE dbo.UserFavorites
    SET ProjectName = COALESCE(@name, ProjectName)
    WHERE UserUpn=@upn AND ProjectNo=@no;
END";
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var upn = cmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
            upn.Value = userUpn ?? (object)DBNull.Value;
            var no = cmd.Parameters.Add("@no", SqlDbType.NVarChar, 256);
            no.Value = projectNo ?? (object)DBNull.Value;
            var name = cmd.Parameters.Add("@name", SqlDbType.NVarChar, 500);
            name.Value = projectName ?? (object)DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoveFavoriteAsync(string userUpn, string projectNo)
        {
            const string sql = @"DELETE FROM dbo.UserFavorites WHERE UserUpn=@upn AND ProjectNo=@no;";
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var upn = cmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
            upn.Value = userUpn ?? (object)DBNull.Value;
            var no = cmd.Parameters.Add("@no", SqlDbType.NVarChar, 256);
            no.Value = projectNo ?? (object)DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Teams =====
        public async Task<List<(Guid TeamId, string Name)>> GetTeamsAsync(string userUpn)
        {
            const string sql = @"
SELECT TeamId, Name
FROM dbo.UserTeams
WHERE UserUpn = @upn
ORDER BY Name;";
            var list = new List<(Guid, string)>();
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var upn = cmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
            upn.Value = userUpn ?? (object)DBNull.Value;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add((r.GetGuid(0), r.GetString(1)));
            return list;
        }

        public async Task<Guid> AddTeamAsync(string userUpn, string name)
        {
            var id = Guid.NewGuid();
            const string sql = @"
INSERT INTO dbo.UserTeams(TeamId, UserUpn, Name, CreatedUtc)
VALUES(@id, @upn, @name, SYSUTCDATETIME());";
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var idParam = cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier);
            idParam.Value = id;
            var upn = cmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
            upn.Value = userUpn ?? (object)DBNull.Value;
            var nameParam = cmd.Parameters.Add("@name", SqlDbType.NVarChar, 500);
            nameParam.Value = name ?? (object)DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
            return id;
        }

        public async Task DeleteTeamAsync(Guid teamId)
        {
            const string sql = @"
DELETE FROM dbo.UserTeamMembers WHERE TeamId=@id;
DELETE FROM dbo.UserTeams WHERE TeamId=@id;";
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var id = cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier);
            id.Value = teamId;
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Members =====
        public async Task<List<(string Email, string? DisplayName)>> GetMembersAsync(Guid teamId)
        {
            const string sql = @"
SELECT Email, DisplayName
FROM dbo.UserTeamMembers
WHERE TeamId=@id
ORDER BY COALESCE(DisplayName,''), Email;";
            var list = new List<(string, string?)>();
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var id = cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier);
            id.Value = teamId;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)));
            return list;
        }

        // All members for all teams owned by a given UserUpn
        public async Task<List<(Guid TeamId, string Email, string? DisplayName)>> GetMembersForUserAsync(string userUpn)
        {
            if (string.IsNullOrWhiteSpace(userUpn))
                return new();

            const string sql = @"
SELECT m.TeamId,
       m.Email,
       m.DisplayName
FROM dbo.UserTeamMembers m
INNER JOIN dbo.UserTeams t ON t.TeamId = m.TeamId
WHERE t.UserUpn = @upn
ORDER BY m.TeamId, COALESCE(m.DisplayName, ''), m.Email;";

            var list = new List<(Guid, string, string?)>();

            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            await using var cmd = new SqlCommand(sql, con);
            var upn = cmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
            upn.Value = userUpn ?? (object)DBNull.Value;

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var teamId = r.GetGuid(0);
                var email = r.GetString(1);
                var display = r.IsDBNull(2) ? null : r.GetString(2);
                list.Add((teamId, email, display));
            }

            return list;
        }

        public async Task AddMemberAsync(Guid teamId, string email, string? displayName)
        {
            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.UserTeamMembers WHERE TeamId=@id AND Email=@em)
    INSERT INTO dbo.UserTeamMembers(TeamId, Email, DisplayName, CreatedUtc)
    VALUES(@id, @em, @name, SYSUTCDATETIME());
ELSE
    UPDATE dbo.UserTeamMembers
    SET DisplayName = COALESCE(@name, DisplayName)
    WHERE TeamId=@id AND Email=@em;";
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var id = cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier);
            id.Value = teamId;
            var em = cmd.Parameters.Add("@em", SqlDbType.NVarChar, 256);
            em.Value = email ?? (object)DBNull.Value;
            var name = cmd.Parameters.Add("@name", SqlDbType.NVarChar, 500);
            name.Value = displayName ?? (object)DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoveMemberAsync(Guid teamId, string email)
        {
            const string sql = @"DELETE FROM dbo.UserTeamMembers WHERE TeamId=@id AND Email=@em;";
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var id = cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier);
            id.Value = teamId;
            var em = cmd.Parameters.Add("@em", SqlDbType.NVarChar, 256);
            em.Value = email ?? (object)DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Autocomplete =====
        public async Task<List<(string ProjectNo, string? ProjectName)>> SearchProjectsAsync(string term, int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(term)) return new();
            var pat = $"%{term.Trim()}%";
            const string sql = @"
SELECT TOP(@limit)
    t.ProjectNo,
    NULLIF(MAX(NULLIF(t.ProjectName, '')), '') AS ProjectName
FROM dbo.Transmittals t
WHERE t.ProjectNo LIKE @pat OR t.ProjectName LIKE @pat
GROUP BY t.ProjectNo
ORDER BY t.ProjectNo;";
            var list = new List<(string, string?)>();
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var patParam = cmd.Parameters.Add("@pat", SqlDbType.NVarChar, 4000);
            patParam.Value = pat ?? (object)DBNull.Value;
            var limitParam = cmd.Parameters.Add("@limit", SqlDbType.Int);
            limitParam.Value = limit;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)));
            return list;
        }

        public async Task<List<(string Email, string? DisplayName)>> SearchPeopleAsync(string userUpn, string term, int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(userUpn) || string.IsNullOrWhiteSpace(term)) return new();
            var pat = $"%{term.Trim()}%";
            const string sql = @"
SELECT TOP(@limit) m.Email,
       NULLIF(MAX(NULLIF(m.DisplayName, '')), '') AS DisplayName
FROM dbo.UserTeamMembers m
INNER JOIN dbo.UserTeams t ON t.TeamId = m.TeamId
WHERE t.UserUpn = @upn AND (m.Email LIKE @pat OR m.DisplayName LIKE @pat)
GROUP BY m.Email
ORDER BY DisplayName NULLS LAST, Email;";
            var list = new List<(string, string?)>();
            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var upn = cmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
            upn.Value = userUpn ?? (object)DBNull.Value;
            var patParam = cmd.Parameters.Add("@pat", SqlDbType.NVarChar, 4000);
            patParam.Value = pat ?? (object)DBNull.Value;
            var limitParam = cmd.Parameters.Add("@limit", SqlDbType.Int);
            limitParam.Value = limit;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)));
            return list;
        }

        // ===== Email signature (UserPreferences.EmailSignatureHtml) =====

        public async Task<string?> GetEmailSignatureHtmlAsync(string userUpn)
        {
            if (string.IsNullOrWhiteSpace(userUpn))
                throw new ArgumentNullException(nameof(userUpn));

            const string sql = @"
SELECT EmailSignatureHtml
FROM dbo.UserPreferences
WHERE UserUpn = @upn;";

            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var upn = cmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
            upn.Value = userUpn ?? (object)DBNull.Value;
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : (string)result;
        }

        public async Task SaveEmailSignatureHtmlAsync(string userUpn, string? emailSignatureHtml)
        {
            if (string.IsNullOrWhiteSpace(userUpn))
                throw new ArgumentNullException(nameof(userUpn));

            const string sql = @"
MERGE dbo.UserPreferences AS target
USING (SELECT @upn AS UserUpn) AS src
    ON target.UserUpn = src.UserUpn
WHEN MATCHED THEN
    UPDATE SET EmailSignatureHtml = @sig,
               ModifiedUtc        = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (UserUpn, AutoFileOnSend, ItemsToFileEnabled, EmailSignatureHtml, CreatedUtc, ModifiedUtc)
    VALUES (@upn, 1, 1, @sig, SYSUTCDATETIME(), SYSUTCDATETIME());";

            await using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            var upn = cmd.Parameters.Add("@upn", SqlDbType.NVarChar, 256);
            upn.Value = userUpn ?? (object)DBNull.Value;
            var sig = cmd.Parameters.Add("@sig", SqlDbType.NVarChar, -1);
            sig.Value = string.IsNullOrWhiteSpace(emailSignatureHtml)
                ? (object)DBNull.Value
                : emailSignatureHtml;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
