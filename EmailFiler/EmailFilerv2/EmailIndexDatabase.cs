using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace EmailFilerv2
{
    public sealed class EmailIndexDatabase : IDisposable
    {
        private readonly string _dbPath;
        private SQLiteConnection _conn;
        private SQLiteTransaction _tx;

        public EmailIndexDatabase(string dbPath)
        {
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                Open();
                InitPragmas();
                InitSchema();     // create if missing
                RunMigrations();  // heal old schemas
            }
            catch (Exception ex)
            {
                // include path in the error so we can verify which DB is being opened
                throw new InvalidOperationException($"Failed to open or migrate DB at:\n{_dbPath}\n\n{ex.Message}", ex);
            }
        }

        private void Open()
        {
            _conn = new SQLiteConnection(new SQLiteConnectionStringBuilder
            {
                DataSource = _dbPath,
                ForeignKeys = true,
                JournalMode = SQLiteJournalModeEnum.Wal
            }.ToString());
            _conn.Open();
        }

        private void InitPragmas()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA temp_store=MEMORY;
                PRAGMA cache_size=-100000; -- ~100MB page cache
            ";
            cmd.ExecuteNonQuery();
        }

        private void InitSchema()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS EmailMetadata (
                  Id INTEGER PRIMARY KEY,
                  FilePath TEXT UNIQUE,
                  FileName TEXT,
                  Sender TEXT,
                  SendDate TEXT,
                  BodyPreview TEXT,
                  LastWriteUtc INTEGER,
                  Length INTEGER
                );

                CREATE INDEX IF NOT EXISTS idx_EmailMetadata_SendDate ON EmailMetadata (SendDate);
                CREATE INDEX IF NOT EXISTS idx_EmailMetadata_Sender   ON EmailMetadata (Sender);
                CREATE INDEX IF NOT EXISTS idx_EmailMetadata_FileName ON EmailMetadata (FileName);
            ";
            cmd.ExecuteNonQuery();

            // Try to create FTS5 table; ignore if SQLite lacks FTS5
            try
            {
                using var fts = _conn.CreateCommand();
                fts.CommandText = @"
                    CREATE VIRTUAL TABLE IF NOT EXISTS EmailBody_fts USING fts5(
                      FilePath UNINDEXED,
                      Body,
                      tokenize = 'porter'
                    );
                ";
                fts.ExecuteNonQuery();
            }
            catch { /* ignore if not supported */ }
        }

        private void RunMigrations()
        {
            // Try simple ALTERs first; if any fails, rebuild table with the correct schema and copy rows over.
            try
            {
                EnsureColumnExists("EmailMetadata", "BodyPreview", "TEXT");
                EnsureColumnExists("EmailMetadata", "LastWriteUtc", "INTEGER");
                EnsureColumnExists("EmailMetadata", "Length", "INTEGER");

                // Ensure FTS exists if available
                try
                {
                    if (!TableExists("EmailBody_fts"))
                    {
                        using var fts = _conn.CreateCommand();
                        fts.CommandText = @"
                            CREATE VIRTUAL TABLE IF NOT EXISTS EmailBody_fts USING fts5(
                              FilePath UNINDEXED,
                              Body,
                              tokenize = 'porter'
                            );
                        ";
                        fts.ExecuteNonQuery();
                    }
                }
                catch { /* ignore */ }
            }
            catch
            {
                // Fall back: rebuild EmailMetadata atomically with the correct schema.
                RebuildEmailMetadataTable();
            }
        }

        private void RebuildEmailMetadataTable()
        {
            using var tx = _conn.BeginTransaction();
            // create new table
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS EmailMetadata_new (
                      Id INTEGER PRIMARY KEY,
                      FilePath TEXT UNIQUE,
                      FileName TEXT,
                      Sender TEXT,
                      SendDate TEXT,
                      BodyPreview TEXT,
                      LastWriteUtc INTEGER,
                      Length INTEGER
                    );";
                cmd.ExecuteNonQuery();
            }

            // copy what we can from old table if it exists
            if (TableExists("EmailMetadata"))
            {
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;

                // Copy only the columns that exist on the old table
                var cols = GetExistingColumns("EmailMetadata");
                bool hasFilePath = cols.Contains("FilePath");
                bool hasFileName = cols.Contains("FileName");
                bool hasSender = cols.Contains("Sender");
                bool hasSendDate = cols.Contains("SendDate");
                bool hasBodyPrev = cols.Contains("BodyPreview");
                bool hasLastWrite = cols.Contains("LastWriteUtc");
                bool hasLength = cols.Contains("Length");

                string selectCols =
                    (hasFilePath ? "FilePath" : "'' AS FilePath") + "," +
                    (hasFileName ? "FileName" : "'' AS FileName") + "," +
                    (hasSender ? "Sender" : "'' AS Sender") + "," +
                    (hasSendDate ? "SendDate" : "'' AS SendDate") + "," +
                    (hasBodyPrev ? "BodyPreview" : "NULL AS BodyPreview") + "," +
                    (hasLastWrite ? "LastWriteUtc" : "0 AS LastWriteUtc") + "," +
                    (hasLength ? "Length" : "0 AS Length");

                cmd.CommandText = $@"
                    INSERT OR IGNORE INTO EmailMetadata_new
                        (FilePath, FileName, Sender, SendDate, BodyPreview, LastWriteUtc, Length)
                    SELECT {selectCols} FROM EmailMetadata;";
                cmd.ExecuteNonQuery();

                // swap tables
                using var dropIdx = _conn.CreateCommand();
                dropIdx.Transaction = tx;
                dropIdx.CommandText = @"
                    DROP TABLE EmailMetadata;
                    ALTER TABLE EmailMetadata_new RENAME TO EmailMetadata;

                    CREATE INDEX IF NOT EXISTS idx_EmailMetadata_SendDate ON EmailMetadata (SendDate);
                    CREATE INDEX IF NOT EXISTS idx_EmailMetadata_Sender   ON EmailMetadata (Sender);
                    CREATE INDEX IF NOT EXISTS idx_EmailMetadata_FileName ON EmailMetadata (FileName);
                ";
                dropIdx.ExecuteNonQuery();
            }
            else
            {
                // No old table — just rename
                using var ren = _conn.CreateCommand();
                ren.Transaction = tx;
                ren.CommandText = "ALTER TABLE EmailMetadata_new RENAME TO EmailMetadata;";
                ren.ExecuteNonQuery();
            }

            tx.Commit();
        }

        private HashSet<string> GetExistingColumns(string table)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r["name"]?.ToString();
                if (!string.IsNullOrEmpty(name)) set.Add(name);
            }
            return set;
        }

        private bool TableExists(string table)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@t LIMIT 1;";
            cmd.Parameters.AddWithValue("@t", table);
            var o = cmd.ExecuteScalar();
            return o != null && o != DBNull.Value;
        }

        private bool ColumnExists(string table, string column)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r["name"]?.ToString();
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void EnsureColumnExists(string table, string column, string type)
        {
            if (!TableExists(table)) return;
            if (ColumnExists(table, column)) return;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
            cmd.ExecuteNonQuery();
        }

        // -------- Transactions / Commands --------
        public void BeginTransaction() { if (_tx == null) _tx = _conn.BeginTransaction(); }
        public void CommitTransaction() { _tx?.Commit(); _tx?.Dispose(); _tx = null; }
        public void RollbackTransaction() { _tx?.Rollback(); _tx?.Dispose(); _tx = null; }
        public SQLiteCommand CreateCommand() { var c = _conn.CreateCommand(); if (_tx != null) c.Transaction = _tx; return c; }

        // -------- Basic Load / Compatibility --------
        public List<EmailMetadata> LoadAllMetadata()
        {
            var list = new List<EmailMetadata>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT FilePath, FileName, Sender, SendDate, BodyPreview FROM EmailMetadata ORDER BY SendDate DESC, FileName ASC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new EmailMetadata
                {
                    FilePath = r.IsDBNull(0) ? null : r.GetString(0),
                    FileName = r.IsDBNull(1) ? null : r.GetString(1),
                    Sender = r.IsDBNull(2) ? null : r.GetString(2),
                    SendDate = r.IsDBNull(3) ? null : r.GetString(3),
                    BodyPreview = r.IsDBNull(4) ? null : r.GetString(4)
                });
            }
            return list;
        }

        public void InsertOrUpdateMetadata(EmailMetadata m)
        {
            if (m == null || string.IsNullOrEmpty(m.FilePath)) return;
            long len = 0, mt = 0;
            try
            {
                var fi = new FileInfo(m.FilePath);
                if (fi.Exists) { len = fi.Length; mt = fi.LastWriteTimeUtc.Ticks; }
            }
            catch { }
            UpsertHeader(m, len, mt);
        }

        // -------- Delta Helpers --------
        public Dictionary<string, (long length, long mtimeTicks)> GetKnownFilesMap()
        {
            var map = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT FilePath, Length, LastWriteUtc FROM EmailMetadata";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string path = r.IsDBNull(0) ? "" : r.GetString(0);
                long len = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                long mt = r.IsDBNull(2) ? 0 : r.GetInt64(2);
                if (!string.IsNullOrEmpty(path)) map[path] = (len, mt);
            }
            return map;
        }

        public void UpsertHeader(EmailMetadata m, long length, long mtimeTicks, SQLiteCommand cachedCmd = null)
        {
            var cmd = cachedCmd ?? CreateCommand();
            cmd.CommandText = @"
                INSERT INTO EmailMetadata (FilePath, FileName, Sender, SendDate, BodyPreview, Length, LastWriteUtc)
                VALUES (@p,@n,@s,@d,@bp,@len,@mt)
                ON CONFLICT(FilePath) DO UPDATE SET
                  FileName     = excluded.FileName,
                  Sender       = excluded.Sender,
                  SendDate     = excluded.SendDate,
                  BodyPreview  = COALESCE(excluded.BodyPreview, EmailMetadata.BodyPreview),
                  Length       = excluded.Length,
                  LastWriteUtc = excluded.LastWriteUtc;";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@p", m.FilePath ?? "");
            cmd.Parameters.AddWithValue("@n", m.FileName ?? "");
            cmd.Parameters.AddWithValue("@s", m.Sender ?? "");
            cmd.Parameters.AddWithValue("@d", m.SendDate ?? "");
            if (m.BodyPreview == null) cmd.Parameters.AddWithValue("@bp", DBNull.Value);
            else cmd.Parameters.AddWithValue("@bp", (object)m.BodyPreview);
            cmd.Parameters.AddWithValue("@len", length);
            cmd.Parameters.AddWithValue("@mt", mtimeTicks);
            cmd.ExecuteNonQuery();
        }

        public void UpsertBodyFts(string filePath, string body, SQLiteCommand cachedCmd = null)
        {
            if (!FtsAvailable()) return;

            var del = cachedCmd ?? CreateCommand();
            del.CommandText = "DELETE FROM EmailBody_fts WHERE FilePath=@p;";
            del.Parameters.Clear();
            del.Parameters.AddWithValue("@p", filePath ?? "");
            del.ExecuteNonQuery();

            var ins = cachedCmd ?? CreateCommand();
            ins.CommandText = "INSERT INTO EmailBody_fts (FilePath, Body) VALUES (@p, @b);";
            ins.Parameters.Clear();
            ins.Parameters.AddWithValue("@p", filePath ?? "");
            ins.Parameters.AddWithValue("@b", body ?? "");
            ins.ExecuteNonQuery();
        }

        public void DeleteMissingExcept(HashSet<string> currentPaths)
        {
            using var tx = _conn.BeginTransaction();

            var toDelete = new List<string>();
            using (var select = _conn.CreateCommand())
            {
                select.Transaction = tx;
                select.CommandText = "SELECT FilePath FROM EmailMetadata";
                using var r = select.ExecuteReader();
                while (r.Read())
                {
                    var p = r.IsDBNull(0) ? null : r.GetString(0);
                    if (!string.IsNullOrEmpty(p) && !currentPaths.Contains(p))
                        toDelete.Add(p);
                }
            }

            if (toDelete.Count > 0)
            {
                using var del = _conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = "DELETE FROM EmailMetadata WHERE FilePath=@p";
                foreach (var p in toDelete)
                {
                    del.Parameters.Clear();
                    del.Parameters.AddWithValue("@p", p);
                    del.ExecuteNonQuery();
                }

                if (FtsAvailable())
                {
                    using var delFts = _conn.CreateCommand();
                    delFts.Transaction = tx;
                    delFts.CommandText = "DELETE FROM EmailBody_fts WHERE FilePath=@p";
                    foreach (var p in toDelete)
                    {
                        delFts.Parameters.Clear();
                        delFts.Parameters.AddWithValue("@p", p);
                        delFts.ExecuteNonQuery();
                    }
                }
            }

            tx.Commit();
        }

        // -------- Search helpers --------
        public List<EmailMetadata> SearchBodyFts(string query)
        {
            var list = new List<EmailMetadata>();
            if (!FtsAvailable() || string.IsNullOrWhiteSpace(query))
                return list;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT m.FilePath, m.FileName, m.Sender, m.SendDate, m.BodyPreview
                FROM EmailMetadata m
                JOIN EmailBody_fts f ON f.FilePath = m.FilePath
                WHERE EmailBody_fts MATCH @q
                ORDER BY m.SendDate DESC;";
            cmd.Parameters.AddWithValue("@q", query);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new EmailMetadata
                {
                    FilePath = r.IsDBNull(0) ? null : r.GetString(0),
                    FileName = r.IsDBNull(1) ? null : r.GetString(1),
                    Sender = r.IsDBNull(2) ? null : r.GetString(2),
                    SendDate = r.IsDBNull(3) ? null : r.GetString(3),
                    BodyPreview = r.IsDBNull(4) ? null : r.GetString(4)
                });
            }
            return list;
        }

        private bool FtsAvailable()
        {
            try
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='EmailBody_fts';";
                var o = cmd.ExecuteScalar();
                return o != null && o != DBNull.Value;
            }
            catch { return false; }
        }

        public (int EmailCount, long TotalBytes) GetStats()
        {
            using var cmd = CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(Length),0) FROM EmailMetadata;";
            using var r = cmd.ExecuteReader(CommandBehavior.SingleRow);
            if (r.Read())
            {
                int count = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                long sum = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                return (count, sum);
            }
            return (0, 0);
        }

        // -------- Rebuild heuristic (compat) --------
        public bool ShouldRebuild(string emailsRoot)
        {
            try
            {
                if (!Directory.Exists(emailsRoot)) return false;

                int dbCount;
                long dbMaxTicks;
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*), COALESCE(MAX(LastWriteUtc),0) FROM EmailMetadata";
                    using var r = cmd.ExecuteReader(CommandBehavior.SingleRow);
                    r.Read();
                    dbCount = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                    dbMaxTicks = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                }

                var files = Directory.GetDirectories(emailsRoot)
                    .SelectMany(d => Directory.EnumerateFiles(d, "*.msg"))
                    .Concat(Directory.GetDirectories(emailsRoot).SelectMany(d => Directory.EnumerateFiles(d, "*.eml")));

                int fsCount = 0;
                long fsMaxTicks = 0;
                foreach (var f in files)
                {
                    fsCount++;
                    try
                    {
                        var t = new FileInfo(f).LastWriteTimeUtc.Ticks;
                        if (t > fsMaxTicks) fsMaxTicks = t;
                    }
                    catch { }
                }

                if (fsCount != dbCount) return true;
                if (fsMaxTicks > dbMaxTicks) return true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        public void Dispose()
        {
            try { _tx?.Dispose(); } catch { }
            try { _conn?.Dispose(); } catch { }
        }
    }
}
