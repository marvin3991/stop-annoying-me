using StopAnnoyingMe.Core.Services;

namespace StopAnnoyingMe.Core.Tests;

public class DuplicateGuardTests
{
    private static readonly DateTime Now = new(2026, 9, 16, 14, 30, 0);

    [Fact]
    public void 沒有前一筆時一律放行()
    {
        var guard = new DuplicateGuard(3);
        Assert.False(guard.IsDuplicate(null, Now));
    }

    [Theory]
    [InlineData(0.0, true)]   // 同一瞬間再按一次
    [InlineData(2.9, true)]   // 還在保護窗內
    [InlineData(3.0, false)]  // 邊界：剛好滿 3 秒放行（規則是「未滿 3 秒」）
    [InlineData(3.1, false)]
    [InlineData(600.0, false)]
    public void 依照未滿門檻秒數的規則判定重複(double elapsedSeconds, bool expected)
    {
        var guard = new DuplicateGuard(3);
        var last = Now.AddSeconds(-elapsedSeconds);

        Assert.Equal(expected, guard.IsDuplicate(last, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void 保護秒數為零或負數時等同關閉保護(int windowSeconds)
    {
        var guard = new DuplicateGuard(windowSeconds);
        Assert.False(guard.IsDuplicate(Now, Now));
    }

    [Fact]
    public void 系統時鐘被往回調時仍然放行()
    {
        // 上一筆比「現在」還晚，代表時鐘被調整過。
        // 這時使用者確實按了按鈕，不該被當成手滑吃掉。
        var guard = new DuplicateGuard(3);
        var last = Now.AddHours(1);

        Assert.False(guard.IsDuplicate(last, Now));
    }
}
