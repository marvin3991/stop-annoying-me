using StopAnnoyingMe.Core.Data;
using StopAnnoyingMe.Core.Models;

namespace StopAnnoyingMe.Core.Services;

public enum RecordOutcome
{
    /// <summary>已寫入一筆新記錄。</summary>
    Recorded,

    /// <summary>距上一筆太近，判定為手滑或熱鍵連發，未寫入。</summary>
    SuppressedAsDuplicate,
}

/// <param name="Outcome">這次觸發的結果。</param>
/// <param name="Item">實際寫入的記錄；被判為重複時為 null。</param>
/// <param name="TodayCount">處理後的今日次數。</param>
public sealed record RecordResult(RecordOutcome Outcome, Interruption? Item, int TodayCount)
{
    public bool Recorded => Outcome == RecordOutcome.Recorded;
}

/// <param name="Undone">是否真的撤銷了一筆。</param>
/// <param name="Removed">被刪掉的那筆記錄。</param>
/// <param name="TodayCount">撤銷後的今日次數。</param>
public sealed record UndoResult(bool Undone, Interruption? Removed, int TodayCount);

/// <summary>
/// 把「記錄一次打擾」這件事的完整規則收在一起：取時間、去重、寫入、重算今日次數。
/// UI 只要呼叫這裡，不必自己拼湊這些步驟。
/// </summary>
public sealed class InterruptionService
{
    private readonly InterruptionRepository _repository;
    private readonly Func<DateTime> _clock;

    /// <param name="repository">資料存取層。</param>
    /// <param name="clock">取得目前本地時間；測試時可注入固定時間。</param>
    public InterruptionService(InterruptionRepository repository, Func<DateTime>? clock = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>記錄一次打擾。</summary>
    /// <param name="duplicateGuardSeconds">手滑保護秒數，0 表示不保護。</param>
    /// <param name="source">可選標籤。</param>
    public RecordResult Record(int duplicateGuardSeconds, string? source = null)
    {
        var now = _clock();
        var today = DateOnly.FromDateTime(now);
        var guard = new DuplicateGuard(duplicateGuardSeconds);
        var last = _repository.GetLast();

        if (guard.IsDuplicate(last?.OccurredAt, now))
        {
            return new RecordResult(RecordOutcome.SuppressedAsDuplicate, null, _repository.CountByDate(today));
        }

        var item = _repository.Add(now, source);
        return new RecordResult(RecordOutcome.Recorded, item, _repository.CountByDate(today));
    }

    /// <summary>撤銷今日最後一筆。今天沒有記錄時什麼都不做。</summary>
    public UndoResult UndoLastToday()
    {
        var today = DateOnly.FromDateTime(_clock());
        var last = _repository.GetLastOfDate(today);

        if (last is null)
        {
            return new UndoResult(false, null, 0);
        }

        var removed = _repository.DeleteById(last.Id);
        return new UndoResult(removed, removed ? last : null, _repository.CountByDate(today));
    }

    /// <summary>把標籤補到今日最後一筆。今天沒有記錄時回傳 false。</summary>
    public bool TagLastToday(string? source)
    {
        var today = DateOnly.FromDateTime(_clock());
        var last = _repository.GetLastOfDate(today);
        return last is not null && _repository.UpdateSource(last.Id, source);
    }

    public int TodayCount() => _repository.CountByDate(DateOnly.FromDateTime(_clock()));

    /// <summary>今日最近幾筆，由新到舊。</summary>
    public IReadOnlyList<Interruption> RecentToday(int limit) =>
        _repository.GetByDate(DateOnly.FromDateTime(_clock()), limit);

    public DateOnly Today() => DateOnly.FromDateTime(_clock());
}
