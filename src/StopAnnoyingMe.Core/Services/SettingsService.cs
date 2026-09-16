using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StopAnnoyingMe.Core.Models;

namespace StopAnnoyingMe.Core.Services;

/// <summary>
/// 讀寫 settings.json。設定檔壞掉絕不能讓程式開不起來，
/// 所以讀取失敗一律改名保留原檔並回落到預設值。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _filePath;

    public SettingsService(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>上一次 Load 因設定檔損毀而改名保留的路徑；正常讀取時為 null。</summary>
    public string? LastRecoveryBackupPath { get; private set; }

    public AppSettings Load()
    {
        LastRecoveryBackupPath = null;

        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
            return (settings ?? new AppSettings()).Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LastRecoveryBackupPath = TryBackupBrokenFile();
            return new AppSettings();
        }
    }

    /// <summary>先寫暫存檔再置換，避免寫到一半當機留下半截設定檔。</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(settings.Normalized(), Options);

        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private string? TryBackupBrokenFile()
    {
        try
        {
            var backupPath = $"{_filePath}.broken-{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Move(_filePath, backupPath, overwrite: true);
            return backupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 連改名都失敗就算了，回落到預設值比擋住啟動重要。
            return null;
        }
    }
}
