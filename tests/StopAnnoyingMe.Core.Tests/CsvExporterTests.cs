using System.Text;
using StopAnnoyingMe.Core.Models;
using StopAnnoyingMe.Core.Services;

namespace StopAnnoyingMe.Core.Tests;

public class CsvExporterTests
{
    private static Interruption Make(DateTime occurredAt, string? source = null, string? note = null) =>
        new(1, occurredAt, source, note, occurredAt);

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("同事", "同事")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("line1\r\nline2", "\"line1\r\nline2\"")]
    public void 欄位跳脫符合CSV規則(string? input, string expected)
    {
        Assert.Equal(expected, CsvExporter.Escape(input));
    }

    [Fact]
    public void 匯出的檔案帶UTF8_BOM讓Excel不亂碼()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.PathTo("export.csv");

        CsvExporter.Export([Make(new DateTime(2026, 9, 16, 14, 32, 5), "同事")], path);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 3);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
    }

    [Fact]
    public void 匯出內容包含標題列與正確的日期時間星期()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.PathTo("export.csv");

        // 2026-09-16 是星期三
        var rows = CsvExporter.Export([Make(new DateTime(2026, 9, 16, 14, 32, 5), "同事", "問報價")], path);

        var lines = File.ReadAllLines(path, Encoding.UTF8);

        Assert.Equal(1, rows);
        Assert.Equal(CsvExporter.Header, lines[0]);
        Assert.Equal("2026-09-16,14:32:05,三,同事,問報價", lines[1]);
    }

    [Fact]
    public void 沒有資料時仍產生只有標題列的檔案()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.PathTo("empty.csv");

        var rows = CsvExporter.Export([], path);

        Assert.Equal(0, rows);
        Assert.Equal(CsvExporter.Header, File.ReadAllLines(path, Encoding.UTF8).Single());
    }

    [Fact]
    public void 含逗號與引號的備註在檔案中被正確包裹()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.PathTo("escape.csv");

        CsvExporter.Export([Make(new DateTime(2026, 9, 16, 9, 0, 0), "同事", "他說「急,很急」的 \"案子\"")], path);

        var content = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("\"他說「急,很急」的 \"\"案子\"\"\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void 目標資料夾不存在時自動建立()
    {
        using var workspace = new TempWorkspace();
        var path = Path.Combine(workspace.Root, "nested", "deep", "export.csv");

        CsvExporter.Export([Make(new DateTime(2026, 9, 16, 9, 0, 0))], path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void 檔案被其他程式鎖住時丟出看得懂的錯誤()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.PathTo("locked.csv");
        using var holder = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        var ex = Assert.Throws<InvalidOperationException>(
            () => CsvExporter.Export([Make(new DateTime(2026, 9, 16, 9, 0, 0))], path));

        Assert.Contains("無法寫入", ex.Message, StringComparison.Ordinal);
        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
    }
}
