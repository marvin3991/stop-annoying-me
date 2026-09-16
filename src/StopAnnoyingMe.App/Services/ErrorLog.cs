using System.Text;

namespace StopAnnoyingMe.App.Services;

/// <summary>
/// 把未預期的錯誤寫進資料夾裡的 error.log，方便事後追查。
/// 記錄失敗絕不能再丟例外出來，否則會變成無窮迴圈。
/// </summary>
internal static class ErrorLog
{
    private const long MaxBytes = 512 * 1024;

    private static readonly Lock Gate = new();

    private static string? _filePath;

    public static void Initialize(string directory) =>
        _filePath = Path.Combine(directory, "error.log");

    public static void Write(string context, Exception exception)
    {
        if (_filePath is null)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                // 檔案過大就整份換掉，避免長期執行把磁碟塞滿。
                if (File.Exists(_filePath) && new FileInfo(_filePath).Length > MaxBytes)
                {
                    File.Move(_filePath, _filePath + ".old", overwrite: true);
                }

                var entry = new StringBuilder()
                    .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                    .AppendLine(context)
                    .AppendLine(exception.ToString())
                    .AppendLine()
                    .ToString();

                File.AppendAllText(_filePath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // 寫不進 log 就算了，不能因此中斷程式。
        }
    }
}
