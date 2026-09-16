using System.Text;
using StopAnnoyingMe.Core.Models;
using StopAnnoyingMe.Core.Services;

namespace StopAnnoyingMe.Core.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void 設定檔不存在時回傳預設值且不建立檔案()
    {
        using var workspace = new TempWorkspace();
        var service = new SettingsService(workspace.SettingsPath);

        var settings = service.Load();

        Assert.Equal("Ctrl+Alt+D", settings.Hotkey);
        Assert.True(settings.AlwaysOnTop);
        Assert.Equal(3, settings.DuplicateGuardSeconds);
        Assert.False(File.Exists(workspace.SettingsPath));
        Assert.Null(service.LastRecoveryBackupPath);
    }

    [Fact]
    public void 儲存後可以原樣讀回()
    {
        using var workspace = new TempWorkspace();
        var service = new SettingsService(workspace.SettingsPath);
        var settings = new AppSettings
        {
            Hotkey = "Ctrl+Shift+Q",
            AlwaysOnTop = false,
            RunAtStartup = true,
            DuplicateGuardSeconds = 10,
            Sources = ["業務", "客戶"],
            WindowLeft = 120.5,
            WindowTop = 40,
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal("Ctrl+Shift+Q", loaded.Hotkey);
        Assert.False(loaded.AlwaysOnTop);
        Assert.True(loaded.RunAtStartup);
        Assert.Equal(10, loaded.DuplicateGuardSeconds);
        Assert.Equal(["業務", "客戶"], loaded.Sources);
        Assert.Equal(120.5, loaded.WindowLeft);
        Assert.Equal(40, loaded.WindowTop);
    }

    [Fact]
    public void 儲存後不會留下暫存檔()
    {
        using var workspace = new TempWorkspace();
        var service = new SettingsService(workspace.SettingsPath);

        service.Save(new AppSettings());

        Assert.False(File.Exists(workspace.SettingsPath + ".tmp"));
    }

    [Fact]
    public void 設定檔內容損毀時改名保留並回落到預設值()
    {
        using var workspace = new TempWorkspace();
        File.WriteAllText(workspace.SettingsPath, "{ 這不是合法 JSON", Encoding.UTF8);
        var service = new SettingsService(workspace.SettingsPath);

        var settings = service.Load();

        Assert.Equal("Ctrl+Alt+D", settings.Hotkey);
        Assert.NotNull(service.LastRecoveryBackupPath);
        Assert.True(File.Exists(service.LastRecoveryBackupPath));
    }

    [Fact]
    public void 欄位缺漏時缺的部分使用預設值()
    {
        using var workspace = new TempWorkspace();
        File.WriteAllText(workspace.SettingsPath, """{ "hotkey": "Ctrl+Alt+K" }""", Encoding.UTF8);
        var service = new SettingsService(workspace.SettingsPath);

        var settings = service.Load();

        Assert.Equal("Ctrl+Alt+K", settings.Hotkey);
        Assert.True(settings.AlwaysOnTop);          // 預設值
        Assert.Equal(3, settings.DuplicateGuardSeconds);
        Assert.NotEmpty(settings.Sources);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99999)]
    public void 不合理的保護秒數會被修正回預設(int stored)
    {
        using var workspace = new TempWorkspace();
        File.WriteAllText(workspace.SettingsPath, $$"""{ "duplicateGuardSeconds": {{stored}} }""", Encoding.UTF8);

        var settings = new SettingsService(workspace.SettingsPath).Load();

        Assert.Equal(3, settings.DuplicateGuardSeconds);
    }

    [Fact]
    public void 標籤清單會去掉空白與重複並限制數量()
    {
        var settings = new AppSettings
        {
            Sources = ["同事", " 同事 ", "", "   ", "主管", "A", "B", "C", "D", "E", "F"],
        }.Normalized();

        Assert.Equal("同事", settings.Sources[0]);
        Assert.Equal(8, settings.Sources.Count);
        Assert.DoesNotContain("", settings.Sources);
        Assert.Equal(settings.Sources.Distinct().Count(), settings.Sources.Count);
    }

    [Fact]
    public void 熱鍵被清空時回落到預設熱鍵()
    {
        var settings = new AppSettings { Hotkey = "   " }.Normalized();

        Assert.Equal("Ctrl+Alt+D", settings.Hotkey);
    }

    [Fact]
    public void 標籤清單全為空白時回落到預設清單()
    {
        var settings = new AppSettings { Sources = ["", "  "] }.Normalized();

        Assert.Equal(5, settings.Sources.Count);
        Assert.Contains("同事", settings.Sources);
    }
}
