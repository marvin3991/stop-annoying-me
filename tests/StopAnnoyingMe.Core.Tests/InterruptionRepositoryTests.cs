using StopAnnoyingMe.Core.Data;

namespace StopAnnoyingMe.Core.Tests;

public class InterruptionRepositoryTests
{
    private static InterruptionRepository CreateRepository(TempWorkspace workspace)
    {
        DatabaseInitializer.Ensure(workspace.DatabasePath);
        return new InterruptionRepository(workspace.DatabasePath);
    }

    [Fact]
    public void 新增後可以讀回相同的時間與標籤()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);
        var occurredAt = new DateTime(2026, 9, 16, 14, 32, 5);

        var added = repository.Add(occurredAt, "同事", "問報價");
        var last = repository.GetLast();

        Assert.True(added.Id > 0);
        Assert.NotNull(last);
        Assert.Equal(occurredAt, last!.OccurredAt);
        Assert.Equal("同事", last.Source);
        Assert.Equal("問報價", last.Note);
    }

    [Fact]
    public void 秒以下的精度會被捨去且回傳值與資料庫一致()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);
        var occurredAt = new DateTime(2026, 9, 16, 14, 32, 5, 987);

        var added = repository.Add(occurredAt);
        var last = repository.GetLast();

        Assert.Equal(new DateTime(2026, 9, 16, 14, 32, 5), added.OccurredAt);
        Assert.Equal(added.OccurredAt, last!.OccurredAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空白標籤一律存成null(string? source)
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);

        repository.Add(new DateTime(2026, 9, 16, 9, 0, 0), source);

        Assert.Null(repository.GetLast()!.Source);
    }

    [Fact]
    public void 空資料庫的查詢回傳空結果而不是丟例外()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);
        var today = new DateOnly(2026, 9, 16);

        Assert.Null(repository.GetLast());
        Assert.Null(repository.GetLastOfDate(today));
        Assert.Null(repository.GetEarliestDate());
        Assert.Empty(repository.GetByDate(today));
        Assert.Empty(repository.GetRange(today, today));
        Assert.Equal(0, repository.CountByDate(today));
        Assert.Equal(0, repository.TotalCount());
        Assert.Equal(24, repository.HourlyDistribution(today, today).Length);
        Assert.All(repository.HourlyDistribution(today, today), value => Assert.Equal(0, value));
    }

    [Fact]
    public void 依日期取回的記錄由新到舊排序且可限制筆數()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);
        var date = new DateOnly(2026, 9, 16);

        for (var hour = 9; hour <= 13; hour++)
        {
            repository.Add(new DateTime(2026, 9, 16, hour, 0, 0));
        }

        var all = repository.GetByDate(date);
        var top3 = repository.GetByDate(date, limit: 3);

        Assert.Equal(5, all.Count);
        Assert.Equal(13, all[0].OccurredAt.Hour);
        Assert.Equal(9, all[^1].OccurredAt.Hour);
        Assert.Equal(3, top3.Count);
        Assert.Equal([13, 12, 11], top3.Select(x => x.OccurredAt.Hour));
    }

    [Fact]
    public void 同一秒的多筆記錄以寫入順序決定先後()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);
        var sameMoment = new DateTime(2026, 9, 16, 11, 0, 0);

        var first = repository.Add(sameMoment, "第一筆");
        var second = repository.Add(sameMoment, "第二筆");

        Assert.Equal(second.Id, repository.GetLast()!.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void 區間查詢包含頭尾兩天且不含區間外的資料()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);

        repository.Add(new DateTime(2026, 9, 14, 10, 0, 0));
        repository.Add(new DateTime(2026, 9, 15, 10, 0, 0));
        repository.Add(new DateTime(2026, 9, 16, 10, 0, 0));
        repository.Add(new DateTime(2026, 9, 17, 10, 0, 0));

        var range = repository.GetRange(new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 16));

        Assert.Equal(2, range.Count);
        Assert.Equal(15, range[0].OccurredAt.Day);
        Assert.Equal(16, range[1].OccurredAt.Day);
        Assert.Equal(2, repository.CountRange(new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 16)));
    }

    [Fact]
    public void 區間起迄顛倒時自動對調不會回傳空結果()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);
        repository.Add(new DateTime(2026, 9, 15, 10, 0, 0));

        Assert.Equal(1, repository.CountRange(new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 14)));
        Assert.Single(repository.GetRange(new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 14)));
    }

    [Fact]
    public void 每小時分佈把記錄放進正確的時段()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);
        var date = new DateOnly(2026, 9, 16);

        repository.Add(new DateTime(2026, 9, 16, 0, 5, 0));   // 凌晨 0 點
        repository.Add(new DateTime(2026, 9, 16, 9, 15, 0));
        repository.Add(new DateTime(2026, 9, 16, 9, 45, 0));
        repository.Add(new DateTime(2026, 9, 16, 23, 59, 59)); // 深夜 23 點

        var buckets = repository.HourlyDistribution(date, date);

        Assert.Equal(24, buckets.Length);
        Assert.Equal(1, buckets[0]);
        Assert.Equal(2, buckets[9]);
        Assert.Equal(1, buckets[23]);
        Assert.Equal(0, buckets[10]);
        Assert.Equal(4, buckets.Sum());
    }

    [Fact]
    public void 刪除指定記錄成功後再刪同一筆回傳false()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);
        var added = repository.Add(new DateTime(2026, 9, 16, 10, 0, 0));

        Assert.True(repository.DeleteById(added.Id));
        Assert.False(repository.DeleteById(added.Id));
        Assert.Equal(0, repository.TotalCount());
    }

    [Fact]
    public void 更新標籤可以設定也可以清除()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);
        var added = repository.Add(new DateTime(2026, 9, 16, 10, 0, 0));

        Assert.True(repository.UpdateSource(added.Id, "主管"));
        Assert.Equal("主管", repository.GetLast()!.Source);

        Assert.True(repository.UpdateSource(added.Id, null));
        Assert.Null(repository.GetLast()!.Source);

        Assert.False(repository.UpdateSource(9999, "不存在"));
    }

    [Fact]
    public void 最早日期回傳全庫最小的日期()
    {
        using var workspace = new TempWorkspace();
        var repository = CreateRepository(workspace);

        repository.Add(new DateTime(2026, 9, 16, 10, 0, 0));
        repository.Add(new DateTime(2026, 3, 2, 10, 0, 0));
        repository.Add(new DateTime(2026, 12, 31, 10, 0, 0));

        Assert.Equal(new DateOnly(2026, 3, 2), repository.GetEarliestDate());
    }
}
