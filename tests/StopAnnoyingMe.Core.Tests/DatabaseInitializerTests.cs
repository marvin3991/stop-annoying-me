using System.Text;
using StopAnnoyingMe.Core.Data;

namespace StopAnnoyingMe.Core.Tests;

public class DatabaseInitializerTests
{
    [Fact]
    public void 首次建立時會產生資料庫檔且不視為復原()
    {
        using var workspace = new TempWorkspace();

        var result = DatabaseInitializer.Ensure(workspace.DatabasePath);

        Assert.False(result.RecoveredFromCorruption);
        Assert.Null(result.BackupPath);
        Assert.True(File.Exists(workspace.DatabasePath));
    }

    [Fact]
    public void 目錄不存在時會自動建立()
    {
        using var workspace = new TempWorkspace();
        var nested = Path.Combine(workspace.Root, "a", "b", "data.db");

        DatabaseInitializer.Ensure(nested);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void 對既有資料庫重複初始化不會遺失資料()
    {
        using var workspace = new TempWorkspace();
        DatabaseInitializer.Ensure(workspace.DatabasePath);

        var repository = new InterruptionRepository(workspace.DatabasePath);
        repository.Add(new DateTime(2026, 9, 16, 9, 0, 0));

        var result = DatabaseInitializer.Ensure(workspace.DatabasePath);

        Assert.False(result.RecoveredFromCorruption);
        Assert.Equal(1, repository.TotalCount());
    }

    [Fact]
    public void 資料庫檔損毀時會改名保留原檔並重建可用的空白資料庫()
    {
        using var workspace = new TempWorkspace();
        File.WriteAllText(workspace.DatabasePath, "這不是 SQLite 檔，是使用者誤蓋上去的垃圾內容", Encoding.UTF8);

        var result = DatabaseInitializer.Ensure(workspace.DatabasePath);

        Assert.True(result.RecoveredFromCorruption);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Contains("這不是 SQLite 檔", File.ReadAllText(result.BackupPath!, Encoding.UTF8), StringComparison.Ordinal);

        // 重建後必須真的能用
        var repository = new InterruptionRepository(workspace.DatabasePath);
        repository.Add(new DateTime(2026, 9, 16, 10, 0, 0));
        Assert.Equal(1, repository.TotalCount());
    }

    [Fact]
    public void 路徑為空字串時丟出參數例外()
    {
        Assert.Throws<ArgumentException>(() => DatabaseInitializer.Ensure("   "));
    }
}
