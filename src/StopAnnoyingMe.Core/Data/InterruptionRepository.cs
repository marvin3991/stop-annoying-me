using System.Globalization;
using Microsoft.Data.Sqlite;
using StopAnnoyingMe.Core.Models;

namespace StopAnnoyingMe.Core.Data;

/// <summary>
/// 打擾記錄的存取層。時間以固定格式的本地時間字串儲存，
/// 讓 SQL 的日期比較與時段擷取都能用單純的字串運算完成。
/// </summary>
public sealed class InterruptionRepository
{
    internal const string TimeFormat = "yyyy-MM-ddTHH:mm:ss";
    internal const string DateFormat = "yyyy-MM-dd";

    private readonly string _connectionString;

    public InterruptionRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = DatabaseInitializer.BuildConnectionString(databasePath);
    }

    public Interruption Add(DateTime occurredAt, string? source = null, string? note = null)
    {
        var createdAt = DateTime.Now;

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO interruptions (occurred_at, occurred_date, source, note, created_at)
            VALUES ($occurredAt, $occurredDate, $source, $note, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$occurredAt", FormatTime(occurredAt));
        command.Parameters.AddWithValue("$occurredDate", FormatDate(DateOnly.FromDateTime(occurredAt)));
        command.Parameters.AddWithValue("$source", Normalize(source));
        command.Parameters.AddWithValue("$note", Normalize(note));
        command.Parameters.AddWithValue("$createdAt", FormatTime(createdAt));

        var id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

        // 回傳與資料庫一致的精度：秒以下會被格式化捨去。
        return new Interruption(
            id,
            TruncateToSecond(occurredAt),
            NormalizeToNull(source),
            NormalizeToNull(note),
            TruncateToSecond(createdAt));
    }

    public bool DeleteById(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM interruptions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>更新指定記錄的標籤。傳入 null 或空字串代表清掉標籤。</summary>
    public bool UpdateSource(long id, string? source)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE interruptions SET source = $source WHERE id = $id;";
        command.Parameters.AddWithValue("$source", Normalize(source));
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 一次更新指定記錄的標籤與備註。兩者傳入 null 或空白都代表清空。
    /// 記錄不存在時回傳 false。
    /// </summary>
    public bool UpdateDetails(long id, string? source, string? note)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE interruptions SET source = $source, note = $note WHERE id = $id;";
        command.Parameters.AddWithValue("$source", Normalize(source));
        command.Parameters.AddWithValue("$note", Normalize(note));
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>依主鍵取回一筆記錄。找不到時回傳 null。</summary>
    public Interruption? GetById(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        return ReadSingle(command);
    }

    /// <summary>全庫最後一筆（依發生時間，同時間則取較晚寫入者）。沒有資料時回傳 null。</summary>
    public Interruption? GetLast()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} ORDER BY occurred_at DESC, id DESC LIMIT 1;";
        return ReadSingle(command);
    }

    public Interruption? GetLastOfDate(DateOnly date)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"{SelectColumns} WHERE occurred_date = $date ORDER BY occurred_at DESC, id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$date", FormatDate(date));
        return ReadSingle(command);
    }

    /// <summary>取某一天的記錄，由新到舊。</summary>
    public IReadOnlyList<Interruption> GetByDate(DateOnly date, int? limit = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"{SelectColumns} WHERE occurred_date = $date ORDER BY occurred_at DESC, id DESC" +
            (limit is > 0 ? " LIMIT $limit;" : ";");
        command.Parameters.AddWithValue("$date", FormatDate(date));
        if (limit is > 0)
        {
            command.Parameters.AddWithValue("$limit", limit.Value);
        }

        return ReadAll(command);
    }

    /// <summary>取某個日期區間的記錄（頭尾都含），由舊到新。</summary>
    public IReadOnlyList<Interruption> GetRange(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            (from, to) = (to, from);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"{SelectColumns} WHERE occurred_date BETWEEN $from AND $to ORDER BY occurred_at ASC, id ASC;";
        command.Parameters.AddWithValue("$from", FormatDate(from));
        command.Parameters.AddWithValue("$to", FormatDate(to));
        return ReadAll(command);
    }

    public int CountByDate(DateOnly date) => CountRange(date, date);

    public int CountRange(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            (from, to) = (to, from);
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM interruptions WHERE occurred_date BETWEEN $from AND $to;";
        command.Parameters.AddWithValue("$from", FormatDate(from));
        command.Parameters.AddWithValue("$to", FormatDate(to));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 區間內每個小時的次數，固定回傳 24 個元素（索引 0-23），沒有記錄的時段為 0。
    /// 直接用字串位置擷取小時，不依賴 SQLite 的日期解析。
    /// </summary>
    public int[] HourlyDistribution(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            (from, to) = (to, from);
        }

        var buckets = new int[24];

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CAST(substr(occurred_at, 12, 2) AS INTEGER) AS hour, COUNT(*)
            FROM interruptions
            WHERE occurred_date BETWEEN $from AND $to
            GROUP BY hour;
            """;
        command.Parameters.AddWithValue("$from", FormatDate(from));
        command.Parameters.AddWithValue("$to", FormatDate(to));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var hour = reader.GetInt32(0);
            if (hour is >= 0 and <= 23)
            {
                buckets[hour] = reader.GetInt32(1);
            }
        }

        return buckets;
    }

    public int TotalCount()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM interruptions;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>最早一筆記錄的日期，用來決定統計畫面能往回看多遠。沒有資料時為 null。</summary>
    public DateOnly? GetEarliestDate()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(occurred_date) FROM interruptions;";
        var value = command.ExecuteScalar();
        if (value is null or DBNull)
        {
            return null;
        }

        return DateOnly.TryParseExact(
            (string)value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private const string SelectColumns =
        "SELECT id, occurred_at, source, note, created_at FROM interruptions";

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static Interruption? ReadSingle(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static List<Interruption> ReadAll(SqliteCommand command)
    {
        var items = new List<Interruption>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    private static Interruption Map(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        ParseTime(reader.GetString(1)),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        ParseTime(reader.GetString(4)));

    internal static string FormatTime(DateTime value) =>
        value.ToString(TimeFormat, CultureInfo.InvariantCulture);

    internal static string FormatDate(DateOnly value) =>
        value.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateTime ParseTime(string value) =>
        DateTime.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateTime.Parse(value, CultureInfo.InvariantCulture);

    private static DateTime TruncateToSecond(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Kind);

    private static object Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string? NormalizeToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
