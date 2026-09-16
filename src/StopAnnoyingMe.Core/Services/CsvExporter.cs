using System.Globalization;
using System.Text;
using StopAnnoyingMe.Core.Models;

namespace StopAnnoyingMe.Core.Services;

/// <summary>匯出 CSV。使用 UTF-8 with BOM，Excel 直接開啟不會變亂碼。</summary>
public static class CsvExporter
{
    public const string Header = "發生日期,發生時間,星期,來源,備註";

    private static readonly string[] WeekdayNames = ["日", "一", "二", "三", "四", "五", "六"];

    /// <summary>把記錄寫成 CSV 檔，回傳寫出的資料列數（不含標題列）。</summary>
    /// <exception cref="InvalidOperationException">檔案被佔用或無寫入權限時丟出，訊息可直接顯示給使用者。</exception>
    public static int Export(IEnumerable<Interruption> items, string filePath)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var builder = new StringBuilder();
        builder.Append(Header).Append("\r\n");

        var rows = 0;
        foreach (var item in items)
        {
            builder
                .Append(Escape(item.OccurredAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(item.OccurredAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(WeekdayNames[(int)item.OccurredAt.DayOfWeek])).Append(',')
                .Append(Escape(item.Source)).Append(',')
                .Append(Escape(item.Note)).Append("\r\n");
            rows++;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"無法寫入「{filePath}」。檔案可能正被其他程式開啟，或沒有寫入權限。原因：{ex.Message}", ex);
        }

        return rows;
    }

    /// <summary>CSV 欄位跳脫：含逗號、雙引號或換行時用雙引號包起來，內部雙引號改成兩個。</summary>
    internal static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuoting = value.Contains(',') || value.Contains('"')
            || value.Contains('\r') || value.Contains('\n');
        if (!needsQuoting)
        {
            return value;
        }

        return string.Concat("\"", value.Replace("\"", "\"\"", StringComparison.Ordinal), "\"");
    }
}
