namespace StopAnnoyingMe.Core.Models;

/// <summary>使用者設定。所有欄位都有可用的預設值，設定檔缺漏或損毀時直接回落到預設。</summary>
public sealed class AppSettings
{
    /// <summary>全域熱鍵，格式為 "Ctrl+Alt+D" 這類修飾鍵加主鍵的組合。</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+D";

    public bool HotkeyEnabled { get; set; } = true;

    public bool AlwaysOnTop { get; set; } = true;

    public bool RunAtStartup { get; set; }

    /// <summary>關閉視窗時最小化到系統匣，而不是結束程式。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>手滑保護：距上一筆未滿這個秒數就不再記錄。設為 0 表示關閉保護。</summary>
    public int DuplicateGuardSeconds { get; set; } = 3;

    /// <summary>可選標籤清單，顯示在主畫面的標籤列。</summary>
    public List<string> Sources { get; set; } = ["同事", "主管", "電話", "會議", "其他"];

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    /// <summary>把不合理的值修正回可用範圍，避免壞設定讓程式行為異常。</summary>
    public AppSettings Normalized()
    {
        if (DuplicateGuardSeconds < 0 || DuplicateGuardSeconds > 3600)
        {
            DuplicateGuardSeconds = 3;
        }

        if (string.IsNullOrWhiteSpace(Hotkey))
        {
            Hotkey = "Ctrl+Alt+D";
        }

        Sources = Sources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();

        if (Sources.Count == 0)
        {
            Sources = ["同事", "主管", "電話", "會議", "其他"];
        }

        return this;
    }
}
