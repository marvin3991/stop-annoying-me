namespace StopAnnoyingMe.Core.Services;

/// <summary>
/// 手滑保護：短時間內連續觸發視為同一次打擾。
/// 熱鍵按住會自動重複觸發，這層保護是必要的。
/// </summary>
public sealed class DuplicateGuard
{
    private readonly int _windowSeconds;

    /// <param name="windowSeconds">保護秒數，0 或負數代表關閉保護。</param>
    public DuplicateGuard(int windowSeconds) => _windowSeconds = windowSeconds;

    public int WindowSeconds => _windowSeconds;

    /// <summary>
    /// 判斷這次觸發是否應該被視為重複。
    /// 剛好等於保護秒數時放行（規則是「未滿 N 秒」）。
    /// 系統時鐘被往回調（now 早於上一筆）時放行，因為那是使用者的真實動作。
    /// </summary>
    public bool IsDuplicate(DateTime? lastOccurredAt, DateTime now)
    {
        if (_windowSeconds <= 0 || lastOccurredAt is null)
        {
            return false;
        }

        var elapsed = (now - lastOccurredAt.Value).TotalSeconds;
        return elapsed >= 0 && elapsed < _windowSeconds;
    }
}
