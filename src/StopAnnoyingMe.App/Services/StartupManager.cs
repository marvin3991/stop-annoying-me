using Microsoft.Win32;

namespace StopAnnoyingMe.App.Services;

/// <summary>
/// 開機自動啟動。寫 HKCU 不需要系統管理員權限，
/// 也不會影響電腦上的其他使用者。
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StopAnnoyingMe";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var stored = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(stored);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // 讀不到登錄檔時保守地回報「沒啟用」，不要讓設定畫面開不起來。
            return false;
        }
    }

    /// <exception cref="InvalidOperationException">寫入登錄檔失敗時丟出，訊息可直接顯示給使用者。</exception>
    public static void SetEnabled(bool enabled)
    {
        var executablePath = Environment.ProcessPath;

        if (enabled && string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("取不到程式的執行檔路徑，無法設定開機自動啟動。");
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("無法開啟開機啟動的登錄檔機碼。");

            if (enabled)
            {
                // 路徑可能含空白，加引號才不會被拆成多個參數。
                key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                $"設定開機自動啟動失敗：{ex.Message}", ex);
        }
    }
}
