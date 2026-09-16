using Microsoft.Data.Sqlite;

namespace StopAnnoyingMe.Core.Data;

/// <param name="RecoveredFromCorruption">原本的資料庫檔無法開啟，已改名保留並重建空白資料庫。</param>
/// <param name="BackupPath">損毀檔被改名後的完整路徑；沒有發生復原時為 null。</param>
public sealed record DatabaseInitResult(bool RecoveredFromCorruption, string? BackupPath);

/// <summary>負責建立與升級資料庫結構，並在檔案損毀時安全復原。</summary>
public static class DatabaseInitializer
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 確保資料庫可用。檔案不存在就建立；檔案損毀就改名保留後重建，
    /// 絕不刪除使用者資料。
    /// </summary>
    public static DatabaseInitResult Ensure(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            CreateSchema(databasePath);
            return new DatabaseInitResult(false, null);
        }
        catch (SqliteException)
        {
            // 走到這裡代表檔案存在但不是可用的 SQLite 資料庫（或內容毀損）。
            // 保留原檔供日後搶救，重建一個空白資料庫讓程式還能用。
            SqliteConnection.ClearAllPools();

            var backupPath = $"{databasePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Move(databasePath, backupPath, overwrite: false);

            // WAL / journal 殘檔會讓重建再次失敗，一併移走。
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            {
                var sidecar = databasePath + suffix;
                if (File.Exists(sidecar))
                {
                    File.Move(sidecar, backupPath + suffix, overwrite: true);
                }
            }

            CreateSchema(databasePath);
            return new DatabaseInitResult(true, backupPath);
        }
    }

    internal static string BuildConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
        }.ToString();

    private static void CreateSchema(string databasePath)
    {
        using var connection = new SqliteConnection(BuildConnectionString(databasePath));
        connection.Open();

        using (var pragma = connection.CreateCommand())
        {
            // 先做一次真正會讀到 header 的操作，損毀檔會在這裡就丟出 SqliteException。
            pragma.CommandText = "PRAGMA journal_mode = WAL;";
            pragma.ExecuteScalar();
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS interruptions (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                occurred_at   TEXT NOT NULL,
                occurred_date TEXT NOT NULL,
                source        TEXT NULL,
                note          TEXT NULL,
                created_at    TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_interruptions_date ON interruptions(occurred_date);
            CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);
            """;
        command.ExecuteNonQuery();

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COUNT(*) FROM schema_version;";
        var rows = Convert.ToInt64(versionCommand.ExecuteScalar());

        if (rows == 0)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO schema_version (version) VALUES ($v);";
            insert.Parameters.AddWithValue("$v", CurrentSchemaVersion);
            insert.ExecuteNonQuery();
        }
    }
}
