using StopAnnoyingMe.Core.Data;

namespace StopAnnoyingMe.Core.Services;

/// <param name="Today">今日次數。</param>
/// <param name="ThisWeek">本週次數（週一起算）。</param>
/// <param name="ThisMonth">本月次數。</param>
/// <param name="Total">全部累計次數。</param>
public sealed record PeriodSummary(int Today, int ThisWeek, int ThisMonth, int Total);

/// <summary>期間統計。所有計算都以呼叫端傳入的「今天」為基準，方便測試跨日與跨月邊界。</summary>
public sealed class StatisticsService
{
    private readonly InterruptionRepository _repository;

    public StatisticsService(InterruptionRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public int CountToday(DateOnly today) => _repository.CountByDate(today);

    public int CountThisWeek(DateOnly today)
    {
        var (start, end) = WeekRange(today);
        return _repository.CountRange(start, end);
    }

    public int CountThisMonth(DateOnly today)
    {
        var (start, end) = MonthRange(today);
        return _repository.CountRange(start, end);
    }

    public PeriodSummary Summarize(DateOnly today) => new(
        CountToday(today),
        CountThisWeek(today),
        CountThisMonth(today),
        _repository.TotalCount());

    public int[] HourlyDistribution(DateOnly from, DateOnly to) =>
        _repository.HourlyDistribution(from, to);

    /// <summary>本週範圍，週一為一週之始（台灣的工作週慣例）。</summary>
    public static (DateOnly Start, DateOnly End) WeekRange(DateOnly date)
    {
        // DayOfWeek 的 Sunday 是 0，先轉成「距離週一幾天」。
        var offsetFromMonday = ((int)date.DayOfWeek + 6) % 7;
        var start = date.AddDays(-offsetFromMonday);
        return (start, start.AddDays(6));
    }

    public static (DateOnly Start, DateOnly End) MonthRange(DateOnly date)
    {
        var start = new DateOnly(date.Year, date.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }
}
