namespace StopAnnoyingMe.Core;

/// <summary>集中管理資料落地位置。測試時傳入暫存目錄即可完全隔離。</summary>
public sealed class AppPaths
{
    public const string FolderName = "StopAnnoyingMe";

    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = rootDirectory;
    }

    public string RootDirectory { get; }

    public string DatabasePath => Path.Combine(RootDirectory, "data.db");

    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    /// <summary>正式執行時的位置：%LOCALAPPDATA%\StopAnnoyingMe。</summary>
    public static AppPaths Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName));

    /// <summary>建立資料夾。無權限或路徑不可用時丟出訊息明確的例外，不靜默失敗。</summary>
    public void EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"無法建立資料夾「{RootDirectory}」，程式無法儲存記錄。原因：{ex.Message}", ex);
        }
    }
}
