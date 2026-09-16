using StopAnnoyingMe.Core.Data;
using StopAnnoyingMe.Core.Services;

namespace StopAnnoyingMe.Core.Tests;

public class StatisticsServiceTests
{
    private static (InterruptionRepository Repository, StatisticsService Statistics) Create(TempWorkspace workspace)
    {
        DatabaseInitializer.Ensure(workspace.DatabasePath);
        var repository = new InterruptionRepository(workspace.DatabasePath);
        return (repository, new StatisticsService(repository));
    }

    [Theory]
    [InlineData("2026-09-14", "2026-09-14", "2026-09-20")] // 週一本身
    [InlineData("2026-09-16", "2026-09-14", "2026-09-20")] // 週三
    [InlineData("2026-09-20", "2026-09-14", "2026-09-20")] // 週日仍屬同一週
    [InlineData("2026-09-21", "2026-09-21", "2026-09-27")] // 跨到下一週
    public void 本週範圍以週一為起點(string today, string expectedStart, string expectedEnd)
    {
        var (start, end) = StatisticsService.WeekRange(DateOnly.Parse(today));

        Assert.Equal(DateOnly.Parse(expectedStart), start);
        Assert.Equal(DateOnly.Parse(expectedEnd), end);
    }

    [Theory]
    [InlineData("2026-02-10", "2026-02-01", "2026-02-28")] // 平年二月
    [InlineData("2024-02-10", "2024-02-01", "2024-02-29")] // 閏年二月
    [InlineData("2026-12-31", "2026-12-01", "2026-12-31")] // 跨年邊界
    public void 本月範圍涵蓋整個月份(string today, string expectedStart, string expectedEnd)
    {
        var (start, end) = StatisticsService.MonthRange(DateOnly.Parse(today));

        Assert.Equal(DateOnly.Parse(expectedStart), start);
        Assert.Equal(DateOnly.Parse(expectedEnd), end);
    }

    [Fact]
    public void 今日次數只算當天不受前後日影響()
    {
        using var workspace = new TempWorkspace();
        var (repository, statistics) = Create(workspace);

        repository.Add(new DateTime(2026, 9, 15, 23, 59, 59)); // 前一天最後一秒
        repository.Add(new DateTime(2026, 9, 16, 0, 0, 0));    // 今天第一秒
        repository.Add(new DateTime(2026, 9, 16, 23, 59, 59)); // 今天最後一秒
        repository.Add(new DateTime(2026, 9, 17, 0, 0, 0));    // 隔天第一秒

        Assert.Equal(2, statistics.CountToday(new DateOnly(2026, 9, 16)));
    }

    [Fact]
    public void 本週統計不把上週日與下週一算進來()
    {
        using var workspace = new TempWorkspace();
        var (repository, statistics) = Create(workspace);

        repository.Add(new DateTime(2026, 9, 13, 12, 0, 0)); // 上週日
        repository.Add(new DateTime(2026, 9, 14, 12, 0, 0)); // 本週一
        repository.Add(new DateTime(2026, 9, 20, 12, 0, 0)); // 本週日
        repository.Add(new DateTime(2026, 9, 21, 12, 0, 0)); // 下週一

        Assert.Equal(2, statistics.CountThisWeek(new DateOnly(2026, 9, 16)));
    }

    [Fact]
    public void 本月統計不把上月底與下月初算進來()
    {
        using var workspace = new TempWorkspace();
        var (repository, statistics) = Create(workspace);

        repository.Add(new DateTime(2026, 8, 31, 12, 0, 0));
        repository.Add(new DateTime(2026, 9, 1, 12, 0, 0));
        repository.Add(new DateTime(2026, 9, 30, 12, 0, 0));
        repository.Add(new DateTime(2026, 10, 1, 12, 0, 0));

        Assert.Equal(2, statistics.CountThisMonth(new DateOnly(2026, 9, 16)));
    }

    [Fact]
    public void 彙總一次回傳今日本週本月與總計()
    {
        using var workspace = new TempWorkspace();
        var (repository, statistics) = Create(workspace);

        repository.Add(new DateTime(2026, 9, 16, 9, 0, 0));  // 今日、本週、本月
        repository.Add(new DateTime(2026, 9, 16, 10, 0, 0)); // 今日、本週、本月
        repository.Add(new DateTime(2026, 9, 14, 9, 0, 0));  // 本週、本月
        repository.Add(new DateTime(2026, 9, 2, 9, 0, 0));   // 本月
        repository.Add(new DateTime(2025, 1, 1, 9, 0, 0));   // 只算總計

        var summary = statistics.Summarize(new DateOnly(2026, 9, 16));

        Assert.Equal(2, summary.Today);
        Assert.Equal(3, summary.ThisWeek);
        Assert.Equal(4, summary.ThisMonth);
        Assert.Equal(5, summary.Total);
    }

    [Fact]
    public void 沒有資料時彙總全為零()
    {
        using var workspace = new TempWorkspace();
        var (_, statistics) = Create(workspace);

        var summary = statistics.Summarize(new DateOnly(2026, 9, 16));

        Assert.Equal(new PeriodSummary(0, 0, 0, 0), summary);
    }

    [Fact]
    public void 時段分佈固定二十四格且沒資料的時段為零()
    {
        using var workspace = new TempWorkspace();
        var (repository, statistics) = Create(workspace);
        var date = new DateOnly(2026, 9, 16);

        repository.Add(new DateTime(2026, 9, 16, 14, 0, 0));

        var buckets = statistics.HourlyDistribution(date, date);

        Assert.Equal(24, buckets.Length);
        Assert.Equal(1, buckets[14]);
        Assert.Equal(23, buckets.Count(value => value == 0));
    }
}
