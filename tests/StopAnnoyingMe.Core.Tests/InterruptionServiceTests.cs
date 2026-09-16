using StopAnnoyingMe.Core.Data;
using StopAnnoyingMe.Core.Services;

namespace StopAnnoyingMe.Core.Tests;

public class InterruptionServiceTests
{
    /// <summary>可控制的時鐘，讓測試能精準模擬連點、跨日等情境。</summary>
    private sealed class FakeClock(DateTime start)
    {
        public DateTime Now { get; set; } = start;

        public Func<DateTime> Func => () => Now;

        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    private static (InterruptionService Service, InterruptionRepository Repository, FakeClock Clock) Create(
        TempWorkspace workspace, DateTime? start = null)
    {
        DatabaseInitializer.Ensure(workspace.DatabasePath);
        var repository = new InterruptionRepository(workspace.DatabasePath);
        var clock = new FakeClock(start ?? new DateTime(2026, 9, 16, 9, 0, 0));
        return (new InterruptionService(repository, clock.Func), repository, clock);
    }

    [Fact]
    public void 記錄一次後今日次數為一()
    {
        using var workspace = new TempWorkspace();
        var (service, _, clock) = Create(workspace);

        var result = service.Record(duplicateGuardSeconds: 3);

        Assert.True(result.Recorded);
        Assert.NotNull(result.Item);
        Assert.Equal(clock.Now, result.Item!.OccurredAt);
        Assert.Equal(1, result.TodayCount);
    }

    [Fact]
    public void 保護窗內連點不會重複寫入且次數不變()
    {
        using var workspace = new TempWorkspace();
        var (service, repository, clock) = Create(workspace);

        service.Record(3);
        clock.Advance(1);
        var second = service.Record(3);

        Assert.Equal(RecordOutcome.SuppressedAsDuplicate, second.Outcome);
        Assert.Null(second.Item);
        Assert.Equal(1, second.TodayCount);
        Assert.Equal(1, repository.TotalCount());
    }

    [Fact]
    public void 超過保護窗後可以再次記錄()
    {
        using var workspace = new TempWorkspace();
        var (service, _, clock) = Create(workspace);

        service.Record(3);
        clock.Advance(3);
        var second = service.Record(3);

        Assert.True(second.Recorded);
        Assert.Equal(2, second.TodayCount);
    }

    [Fact]
    public void 保護秒數設為零時連點都會記錄()
    {
        using var workspace = new TempWorkspace();
        var (service, _, _) = Create(workspace);

        service.Record(0);
        var second = service.Record(0);

        Assert.True(second.Recorded);
        Assert.Equal(2, second.TodayCount);
    }

    [Fact]
    public void 記錄時可以直接帶入標籤()
    {
        using var workspace = new TempWorkspace();
        var (service, _, _) = Create(workspace);

        var result = service.Record(3, "電話");

        Assert.Equal("電話", result.Item!.Source);
    }

    [Fact]
    public void 跨過午夜後今日次數重新計算()
    {
        using var workspace = new TempWorkspace();
        var (service, _, clock) = Create(workspace, new DateTime(2026, 9, 16, 23, 59, 55));

        var beforeMidnight = service.Record(3);
        clock.Advance(10); // 跨到 9/17 00:00:05
        var afterMidnight = service.Record(3);

        Assert.Equal(1, beforeMidnight.TodayCount);
        Assert.Equal(1, afterMidnight.TodayCount);
        Assert.Equal(new DateOnly(2026, 9, 17), service.Today());
    }

    [Fact]
    public void 撤銷會刪掉今日最後一筆()
    {
        using var workspace = new TempWorkspace();
        var (service, repository, clock) = Create(workspace);

        service.Record(0);
        clock.Advance(60);
        var last = service.Record(0);

        var undo = service.UndoLastToday();

        Assert.True(undo.Undone);
        Assert.Equal(last.Item!.Id, undo.Removed!.Id);
        Assert.Equal(1, undo.TodayCount);
        Assert.Equal(1, repository.TotalCount());
    }

    [Fact]
    public void 今天沒有記錄時撤銷不動到昨天的資料()
    {
        using var workspace = new TempWorkspace();
        var (service, repository, _) = Create(workspace);
        repository.Add(new DateTime(2026, 9, 15, 10, 0, 0)); // 昨天的記錄

        var undo = service.UndoLastToday();

        Assert.False(undo.Undone);
        Assert.Null(undo.Removed);
        Assert.Equal(1, repository.TotalCount());
    }

    [Fact]
    public void 補標籤會套用到今日最後一筆()
    {
        using var workspace = new TempWorkspace();
        var (service, _, clock) = Create(workspace);

        service.Record(0);
        clock.Advance(60);
        service.Record(0);

        Assert.True(service.TagLastToday("主管"));

        var recent = service.RecentToday(2);
        Assert.Equal("主管", recent[0].Source);
        Assert.Null(recent[1].Source);
    }

    [Fact]
    public void 今天沒有記錄時補標籤回傳false()
    {
        using var workspace = new TempWorkspace();
        var (service, _, _) = Create(workspace);

        Assert.False(service.TagLastToday("主管"));
    }

    [Fact]
    public void 可以編輯指定記錄的來源與備註()
    {
        using var workspace = new TempWorkspace();
        var (service, _, clock) = Create(workspace);

        var first = service.Record(0).Item!;
        clock.Advance(60);
        service.Record(0);

        // 編輯的是第一筆（不是最後一筆），驗證編輯確實依 id 而非「最後一筆」
        Assert.True(service.UpdateDetails(first.Id, "主管", "臨時插件"));

        var reloaded = service.GetById(first.Id)!;
        Assert.Equal("主管", reloaded.Source);
        Assert.Equal("臨時插件", reloaded.Note);

        var recent = service.RecentToday(5);
        Assert.Null(recent[0].Source);   // 最後一筆沒被動到
    }

    [Fact]
    public void 編輯不存在的記錄回傳false()
    {
        using var workspace = new TempWorkspace();
        var (service, _, _) = Create(workspace);

        Assert.False(service.UpdateDetails(9999, "主管", "備註"));
        Assert.Null(service.GetById(9999));
    }

    [Fact]
    public void 標籤可以反覆切換開關()
    {
        using var workspace = new TempWorkspace();
        var (service, _, _) = Create(workspace);
        service.Record(0);

        // 這是主畫面標籤列的行為：點一次標上、再點一次清掉、再點又標上
        Assert.True(service.TagLastToday("主管"));
        Assert.Equal("主管", service.RecentToday(1)[0].Source);

        Assert.True(service.TagLastToday(null));
        Assert.Null(service.RecentToday(1)[0].Source);

        Assert.True(service.TagLastToday("主管"));
        Assert.Equal("主管", service.RecentToday(1)[0].Source);

        Assert.True(service.TagLastToday("電話"));
        Assert.Equal("電話", service.RecentToday(1)[0].Source);
    }

    [Fact]
    public void 可以刪除中間某一筆而不影響其他筆()
    {
        using var workspace = new TempWorkspace();
        var (service, repository, clock) = Create(workspace);

        var first = service.Record(0).Item!;
        clock.Advance(60);
        var middle = service.Record(0).Item!;
        clock.Advance(60);
        var last = service.Record(0).Item!;

        Assert.True(service.Delete(middle.Id));

        Assert.Equal(2, repository.TotalCount());
        Assert.Null(service.GetById(middle.Id));
        Assert.NotNull(service.GetById(first.Id));
        Assert.NotNull(service.GetById(last.Id));
        Assert.Equal(2, service.TodayCount());
    }

    [Fact]
    public void 重複刪除同一筆第二次回傳false()
    {
        using var workspace = new TempWorkspace();
        var (service, _, _) = Create(workspace);
        var item = service.Record(0).Item!;

        Assert.True(service.Delete(item.Id));
        Assert.False(service.Delete(item.Id));
    }

    [Fact]
    public void 刪除不存在的記錄回傳false()
    {
        using var workspace = new TempWorkspace();
        var (service, _, _) = Create(workspace);

        Assert.False(service.Delete(9999));
    }

    [Fact]
    public void 刪除昨天的記錄不影響今日次數()
    {
        using var workspace = new TempWorkspace();
        var (service, repository, _) = Create(workspace);
        var yesterday = repository.Add(new DateTime(2026, 9, 15, 10, 0, 0));
        service.Record(0);

        Assert.True(service.Delete(yesterday.Id));

        Assert.Equal(1, service.TodayCount());
        Assert.Equal(1, repository.TotalCount());
    }

    [Fact]
    public void 刪掉今天全部記錄後次數歸零且不能再撤銷()
    {
        using var workspace = new TempWorkspace();
        var (service, _, _) = Create(workspace);
        var only = service.Record(0).Item!;

        Assert.True(service.Delete(only.Id));

        Assert.Equal(0, service.TodayCount());
        Assert.Empty(service.RecentToday(5));
        Assert.False(service.UndoLastToday().Undone);
    }

    [Fact]
    public void 編輯記錄不會改動發生時間()
    {
        using var workspace = new TempWorkspace();
        var (service, _, _) = Create(workspace);
        var item = service.Record(0).Item!;

        service.UpdateDetails(item.Id, "會議", "週會被叫走");

        Assert.Equal(item.OccurredAt, service.GetById(item.Id)!.OccurredAt);
    }

    [Fact]
    public void 最近記錄由新到舊且只取今天的()
    {
        using var workspace = new TempWorkspace();
        var (service, repository, clock) = Create(workspace);
        repository.Add(new DateTime(2026, 9, 15, 10, 0, 0)); // 昨天

        for (var i = 0; i < 7; i++)
        {
            service.Record(0);
            clock.Advance(60);
        }

        var recent = service.RecentToday(5);

        Assert.Equal(5, recent.Count);
        Assert.All(recent, item => Assert.Equal(new DateOnly(2026, 9, 16), DateOnly.FromDateTime(item.OccurredAt)));
        Assert.True(recent[0].OccurredAt > recent[^1].OccurredAt);
    }
}
