using LumaClip.Models;
using Microsoft.Data.Sqlite;

namespace LumaClip.Services;

public sealed class DatabaseService : IDisposable
{
    readonly string _connectionString;
    readonly SemaphoreSlim _gate = new(1, 1);
    public string DatabasePath { get; }

    public DatabaseService(string path)
    {
        DatabasePath = path;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
    }

    SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=4000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_info(version INTEGER NOT NULL);
                INSERT INTO schema_info(version) SELECT 1 WHERE NOT EXISTS(SELECT 1 FROM schema_info);
                CREATE TABLE IF NOT EXISTS clips(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    kind INTEGER NOT NULL,
                    text_value TEXT NOT NULL DEFAULT '',
                    content_path TEXT NULL,
                    thumbnail_path TEXT NULL,
                    hash TEXT NOT NULL,
                    source_app TEXT NOT NULL DEFAULT '',
                    source_process TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL,
                    last_copied_at TEXT NOT NULL,
                    copy_count INTEGER NOT NULL DEFAULT 1,
                    is_favorite INTEGER NOT NULL DEFAULT 0,
                    is_pinned INTEGER NOT NULL DEFAULT 0,
                    is_sensitive INTEGER NOT NULL DEFAULT 0,
                    tags TEXT NOT NULL DEFAULT '',
                    note TEXT NOT NULL DEFAULT '',
                    deleted_at TEXT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ix_clips_hash_active ON clips(hash) WHERE deleted_at IS NULL;
                CREATE INDEX IF NOT EXISTS ix_clips_order ON clips(deleted_at, is_pinned DESC, last_copied_at DESC);
                CREATE INDEX IF NOT EXISTS ix_clips_kind ON clips(kind, deleted_at, last_copied_at DESC);
                CREATE INDEX IF NOT EXISTS ix_clips_flags ON clips(is_favorite, is_pinned, deleted_at);
                """;
            await command.ExecuteNonQueryAsync();
        } finally { _gate.Release(); }
    }

    public async Task<ClipboardItem> UpsertAsync(ClipboardItem item)
    {
        await _gate.WaitAsync();
        try {
            using var connection = Open();
            using var find = connection.CreateCommand();
            find.CommandText = "SELECT id,is_favorite,is_pinned,tags,note,copy_count,created_at FROM clips WHERE hash=$hash AND deleted_at IS NULL LIMIT 1;";
            find.Parameters.AddWithValue("$hash", item.Hash);
            using var reader = await find.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                item.Id = reader.GetInt64(0);
                item.IsFavorite = reader.GetInt64(1) != 0;
                item.IsPinned = reader.GetInt64(2) != 0;
                item.Tags = reader.GetString(3);
                item.Note = reader.GetString(4);
                item.CopyCount = reader.GetInt32(5) + 1;
                item.CreatedAt = DateTime.Parse(reader.GetString(6));
                await reader.DisposeAsync();
                using var update = connection.CreateCommand();
                update.CommandText = """
                    UPDATE clips SET last_copied_at=$last,copy_count=copy_count+1,source_app=$app,source_process=$process,
                    kind=$kind,text_value=$text,content_path=COALESCE($content,content_path),thumbnail_path=COALESCE($thumb,thumbnail_path),
                    is_sensitive=$sensitive WHERE id=$id;
                    """;
                BindCommon(update, item);
                update.Parameters.AddWithValue("$id", item.Id);
                await update.ExecuteNonQueryAsync();
                return item;
            }
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO clips(kind,text_value,content_path,thumbnail_path,hash,source_app,source_process,created_at,last_copied_at,
                copy_count,is_favorite,is_pinned,is_sensitive,tags,note)
                VALUES($kind,$text,$content,$thumb,$hash,$app,$process,$created,$last,1,0,0,$sensitive,'','');
                SELECT last_insert_rowid();
                """;
            BindCommon(insert, item);
            item.Id = (long)(await insert.ExecuteScalarAsync() ?? 0L);
            return item;
        } finally { _gate.Release(); }
    }

    static void BindCommon(SqliteCommand command, ClipboardItem item)
    {
        command.Parameters.AddWithValue("$kind", (int)item.Kind);
        command.Parameters.AddWithValue("$text", item.Text ?? "");
        command.Parameters.AddWithValue("$content", (object?)item.ContentPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$thumb", (object?)item.ThumbnailPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", item.Hash);
        command.Parameters.AddWithValue("$app", item.SourceApp ?? "");
        command.Parameters.AddWithValue("$process", item.SourceProcess ?? "");
        command.Parameters.AddWithValue("$created", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$last", item.LastCopiedAt.ToString("O"));
        command.Parameters.AddWithValue("$sensitive", item.IsSensitive ? 1 : 0);
    }

    public async Task<List<ClipboardItem>> QueryAsync(string search = "", string filter = "all", bool recycleBin = false, int limit = 500)
    {
        await _gate.WaitAsync();
        try {
            using var connection = Open();
            using var command = connection.CreateCommand();
            var where = new List<string> { recycleBin ? "deleted_at IS NOT NULL" : "deleted_at IS NULL" };
            if (!string.IsNullOrWhiteSpace(search)) {
                where.Add("(text_value LIKE $q ESCAPE '\\' OR tags LIKE $q ESCAPE '\\' OR note LIKE $q ESCAPE '\\' OR source_app LIKE $q ESCAPE '\\')");
                command.Parameters.AddWithValue("$q", $"%{EscapeLike(search.Trim())}%");
            }
            switch (filter) {
                case "text": where.Add("kind IN (0,1,2)"); break;
                case "image": where.Add("kind=3"); break;
                case "link": where.Add("kind=1"); break;
                case "files": where.Add("kind IN (4,5,6)"); break;
                case "favorite": where.Add("is_favorite=1"); break;
                case "pinned": where.Add("is_pinned=1"); break;
            }
            command.CommandText = $"""
                SELECT id,kind,text_value,content_path,thumbnail_path,hash,source_app,source_process,created_at,last_copied_at,
                copy_count,is_favorite,is_pinned,is_sensitive,tags,note,deleted_at
                FROM clips WHERE {string.Join(" AND ", where)}
                ORDER BY is_pinned DESC,last_copied_at DESC LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
            var result = new List<ClipboardItem>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Add(Read(reader));
            return result;
        } finally { _gate.Release(); }
    }

    static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    static ClipboardItem Read(SqliteDataReader r) => new() {
        Id = r.GetInt64(0), Kind = (ClipKind)r.GetInt32(1), Text = r.GetString(2),
        ContentPath = r.IsDBNull(3) ? null : r.GetString(3), ThumbnailPath = r.IsDBNull(4) ? null : r.GetString(4),
        Hash = r.GetString(5), SourceApp = r.GetString(6), SourceProcess = r.GetString(7),
        CreatedAt = DateTime.Parse(r.GetString(8)), LastCopiedAt = DateTime.Parse(r.GetString(9)), CopyCount = r.GetInt32(10),
        IsFavorite = r.GetInt64(11) != 0, IsPinned = r.GetInt64(12) != 0, IsSensitive = r.GetInt64(13) != 0,
        Tags = r.GetString(14), Note = r.GetString(15), DeletedAt = r.IsDBNull(16) ? null : DateTime.Parse(r.GetString(16))
    };

    public Task SetFavoriteAsync(long id, bool value) => ExecuteAsync("UPDATE clips SET is_favorite=$v WHERE id=$id", id, value);
    public Task SetPinnedAsync(long id, bool value) => ExecuteAsync("UPDATE clips SET is_pinned=$v WHERE id=$id", id, value);
    public async Task UpdateMetadataAsync(long id, string tags, string note)
    {
        await _gate.WaitAsync();
        try {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE clips SET tags=$tags,note=$note WHERE id=$id";
            command.Parameters.AddWithValue("$tags", tags);
            command.Parameters.AddWithValue("$note", note);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync();
        } finally { _gate.Release(); }
    }
    public Task MoveToTrashAsync(long id) => ExecuteAsync("UPDATE clips SET deleted_at=$now,is_pinned=0 WHERE id=$id", id, DateTime.Now.ToString("O"));
    public async Task RestoreAsync(long id)
    {
        await _gate.WaitAsync();
        try {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            using var restore = connection.CreateCommand();
            restore.Transaction = tx;
            restore.CommandText = """
                UPDATE clips SET deleted_at=NULL WHERE id=$id AND NOT EXISTS(
                  SELECT 1 FROM clips active JOIN clips trashed ON trashed.id=$id
                  WHERE active.deleted_at IS NULL AND active.hash=trashed.hash AND active.id<>trashed.id);
                DELETE FROM clips WHERE id=$id AND deleted_at IS NOT NULL AND EXISTS(
                  SELECT 1 FROM clips active JOIN clips trashed ON trashed.id=$id
                  WHERE active.deleted_at IS NULL AND active.hash=trashed.hash AND active.id<>trashed.id);
                """;
            restore.Parameters.AddWithValue("$id", id);
            await restore.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        } finally { _gate.Release(); }
    }
    public Task ClearHistoryAsync() => ExecuteRawAsync("UPDATE clips SET deleted_at=datetime('now'),is_pinned=0 WHERE deleted_at IS NULL;");

    async Task ExecuteAsync(string sql, long id, object value)
    {
        await _gate.WaitAsync();
        try {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", id);
            if (sql.Contains("$v")) command.Parameters.AddWithValue("$v", Convert.ToBoolean(value) ? 1 : 0);
            if (sql.Contains("$now")) command.Parameters.AddWithValue("$now", value);
            await command.ExecuteNonQueryAsync();
        } finally { _gate.Release(); }
    }
    async Task ExecuteRawAsync(string sql)
    {
        await _gate.WaitAsync();
        try { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
        finally { _gate.Release(); }
    }

    public async Task EmptyTrashAsync()
    {
        await _gate.WaitAsync();
        try {
            using var connection = Open();
            var paths = new List<string>();
            using (var read = connection.CreateCommand()) {
                read.CommandText = """
                    SELECT d.content_path,d.thumbnail_path FROM clips d WHERE d.deleted_at IS NOT NULL
                    AND NOT EXISTS(SELECT 1 FROM clips a WHERE a.deleted_at IS NULL AND
                      (a.content_path=d.content_path OR a.thumbnail_path=d.thumbnail_path));
                    """;
                using var r = await read.ExecuteReaderAsync();
                while (await r.ReadAsync()) {
                    if (!r.IsDBNull(0)) paths.Add(r.GetString(0));
                    if (!r.IsDBNull(1)) paths.Add(r.GetString(1));
                }
            }
            using (var delete = connection.CreateCommand()) { delete.CommandText = "DELETE FROM clips WHERE deleted_at IS NOT NULL"; await delete.ExecuteNonQueryAsync(); }
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                try { if (File.Exists(path)) File.Delete(path); } catch { }
        } finally { _gate.Release(); }
    }

    public async Task TrimAsync(int maxItems, int retentionDays)
    {
        await _gate.WaitAsync();
        try {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                UPDATE clips SET deleted_at=datetime('now') WHERE id IN (
                  SELECT id FROM clips WHERE deleted_at IS NULL AND is_favorite=0 AND is_pinned=0
                  ORDER BY last_copied_at DESC LIMIT -1 OFFSET $max);
                """ + (retentionDays > 0 ? "\nUPDATE clips SET deleted_at=datetime('now') WHERE deleted_at IS NULL AND is_favorite=0 AND is_pinned=0 AND last_copied_at < datetime('now',$days);" : "");
            cmd.Parameters.AddWithValue("$max", Math.Max(100, maxItems));
            if (retentionDays > 0) cmd.Parameters.AddWithValue("$days", $"-{retentionDays} days");
            await cmd.ExecuteNonQueryAsync();
        } finally { _gate.Release(); }
    }

    public async Task RecordReuseAsync(long id)
    {
        await _gate.WaitAsync();
        try {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE clips SET last_copied_at=$now,copy_count=copy_count+1 WHERE id=$id";
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        } finally { _gate.Release(); }
    }

    public async Task<(int Active, int Deleted)> CountAsync()
    {
        await _gate.WaitAsync();
        try {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT SUM(CASE WHEN deleted_at IS NULL THEN 1 ELSE 0 END),SUM(CASE WHEN deleted_at IS NOT NULL THEN 1 ELSE 0 END) FROM clips";
            using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? (r.IsDBNull(0) ? 0 : r.GetInt32(0), r.IsDBNull(1) ? 0 : r.GetInt32(1)) : (0, 0);
        } finally { _gate.Release(); }
    }

    public async Task CheckpointAsync()
    {
        await _gate.WaitAsync();
        try { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);"; await cmd.ExecuteNonQueryAsync(); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Creates a transactionally consistent SQLite copy without directly reading
    /// a file that SQLite currently owns on Windows.
    /// </summary>
    public async Task CopyDatabaseToAsync(string destinationPath)
    {
        await _gate.WaitAsync();
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var temporaryPath = destinationPath + ".migrating";
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }

            // Dispose both SQLite handles before replacing the temporary file;
            // Windows otherwise keeps the file handle open even after Close().
            using (var source = Open())
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString())) {
                destination.Open();
                source.BackupDatabase(destination);
            }
            File.Move(temporaryPath, destinationPath, true);
        } finally { _gate.Release(); }
    }
    public void Dispose() => _gate.Dispose();
}
