using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace EmailFilerv2
{
    /// <summary>
    /// Minimal repository for reading/writing favorites from dbo.UserFavorites
    /// in the KorTransmittals database. Safe for the Outlook add-in (.NET 4.8).
    /// </summary>
    internal sealed class SqlFavoritesRepository
    {
        private readonly string _connectionString;

        public SqlFavoritesRepository(string connectionString = null)
        {
            _connectionString = !string.IsNullOrWhiteSpace(connectionString)
                ? connectionString
                : ResolveConnectionString();
        }

        /// <summary>
        /// Prefer the named "KorTransmittals" connection string.
        /// If it does not exist, fall back to the first non-empty connection string.
        /// </summary>
        private static string ResolveConnectionString()
        {
            var all = ConfigurationManager.ConnectionStrings;
            if (all == null || all.Count == 0)
                return string.Empty;

            // 1) Prefer explicit KorTransmittals entry
            var korTrans = all["KorTransmittals"];
            if (korTrans != null && !string.IsNullOrWhiteSpace(korTrans.ConnectionString))
                return korTrans.ConnectionString;

            // 2) Fallback – first non-empty (old behaviour)
            foreach (ConnectionStringSettings s in all)
            {
                if (!string.IsNullOrWhiteSpace(s.ConnectionString))
                    return s.ConnectionString;
            }

            return string.Empty; // SqlConnection will throw if this is invalid
        }

        // Simple POCO to hold a favorite project.
        internal sealed class FavoriteProject
        {
            public string ProjectNo { get; set; }
            public string ProjectName { get; set; } // may be null
        }

        // --------------- Public sync API (used by ribbon) ---------------

        public List<FavoriteProject> GetFavorites(string userUpn)
        {
            return GetFavoritesAsync(userUpn).GetAwaiter().GetResult();
        }

        public void AddFavorite(string userUpn, string projectNo, string projectName)
        {
            AddFavoriteAsync(userUpn, projectNo, projectName).GetAwaiter().GetResult();
        }

        public void RemoveFavorite(string userUpn, string projectNo)
        {
            RemoveFavoriteAsync(userUpn, projectNo).GetAwaiter().GetResult();
        }

        // --------------- Async core ---------------

        public async Task<List<FavoriteProject>> GetFavoritesAsync(string userUpn)
        {
            const string sql = @"
SELECT ProjectNo, ProjectName
FROM dbo.UserFavorites
WHERE UserUpn = @upn
ORDER BY ProjectNo;";

            var list = new List<FavoriteProject>();

            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@upn", (object)(userUpn ?? string.Empty));

                await cn.OpenAsync().ConfigureAwait(false);

                using (var rd = await cmd.ExecuteReaderAsync(CommandBehavior.Default).ConfigureAwait(false))
                {
                    while (await rd.ReadAsync().ConfigureAwait(false))
                    {
                        string projNo = rd.GetString(0);
                        string projName = rd.IsDBNull(1) ? null : rd.GetString(1);

                        list.Add(new FavoriteProject
                        {
                            ProjectNo = projNo,
                            ProjectName = projName
                        });
                    }
                }
            }

            return list;
        }

        public async Task AddFavoriteAsync(string userUpn, string projectNo, string projectName)
        {
            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.UserFavorites WHERE UserUpn = @upn AND ProjectNo = @no)
BEGIN
    INSERT INTO dbo.UserFavorites (UserUpn, ProjectNo, ProjectName, CreatedUtc)
    VALUES (@upn, @no, @name, SYSUTCDATETIME());
END
ELSE
BEGIN
    UPDATE dbo.UserFavorites
    SET ProjectName = COALESCE(@name, ProjectName)
    WHERE UserUpn = @upn AND ProjectNo = @no;
END";

            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@upn", (object)(userUpn ?? string.Empty));
                cmd.Parameters.AddWithValue("@no", (object)(projectNo ?? string.Empty));
                cmd.Parameters.AddWithValue("@name",
                    string.IsNullOrWhiteSpace(projectName) ? (object)DBNull.Value : projectName);

                await cn.OpenAsync().ConfigureAwait(false);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        public async Task RemoveFavoriteAsync(string userUpn, string projectNo)
        {
            const string sql = @"DELETE FROM dbo.UserFavorites WHERE UserUpn = @upn AND ProjectNo = @no;";

            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@upn", (object)(userUpn ?? string.Empty));
                cmd.Parameters.AddWithValue("@no", (object)(projectNo ?? string.Empty));

                await cn.OpenAsync().ConfigureAwait(false);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }
}
