using StopAnnoyingMe.Core.Models;

namespace StopAnnoyingMe.Core.Tests;

public class HotkeyDefinitionTests
{
    [Theory]
    [InlineData("Ctrl+Alt+D", HotkeyModifiers.Control | HotkeyModifiers.Alt, "D", 0x44)]
    [InlineData("ctrl+alt+d", HotkeyModifiers.Control | HotkeyModifiers.Alt, "D", 0x44)]
    [InlineData("CTRL + SHIFT + Q", HotkeyModifiers.Control | HotkeyModifiers.Shift, "Q", 0x51)]
    [InlineData("Alt+1", HotkeyModifiers.Alt, "1", 0x31)]
    [InlineData("Ctrl+F12", HotkeyModifiers.Control, "F12", 0x7B)]
    [InlineData("Win+Space", HotkeyModifiers.Win, "SPACE", 0x20)]
    [InlineData("Control+Insert", HotkeyModifiers.Control, "INSERT", 0x2D)]
    public void 可解析常見的熱鍵組合(string input, HotkeyModifiers modifiers, string key, int virtualKey)
    {
        Assert.True(HotkeyDefinition.TryParse(input, out var definition));

        Assert.Equal(modifiers, definition!.Modifiers);
        Assert.Equal(key, definition.Key);
        Assert.Equal(virtualKey, definition.VirtualKeyCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("D")]           // 沒有修飾鍵，會霸佔一般打字
    [InlineData("Ctrl")]        // 只有修飾鍵
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]    // 兩個主鍵
    [InlineData("Ctrl+F25")]    // 超出 F1-F24
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+@")]      // 不支援的符號鍵
    [InlineData("Ctrl+NotAKey")]
    public void 無效的熱鍵字串一律解析失敗(string? input)
    {
        Assert.False(HotkeyDefinition.TryParse(input, out var definition));
        Assert.Null(definition);
    }

    [Theory]
    [InlineData("Ctrl+Alt+D", "Ctrl+Alt+D")]
    [InlineData("alt+ctrl+d", "Ctrl+Alt+D")]          // 修飾鍵順序正規化
    [InlineData("Win+Shift+Ctrl+F5", "Ctrl+Shift+Win+F5")]
    public void 轉回字串時修飾鍵順序一致(string input, string expected)
    {
        Assert.True(HotkeyDefinition.TryParse(input, out var definition));
        Assert.Equal(expected, definition!.ToString());
    }

    [Fact]
    public void 重複指定同一個修飾鍵不會出錯()
    {
        Assert.True(HotkeyDefinition.TryParse("Ctrl+Ctrl+D", out var definition));
        Assert.Equal(HotkeyModifiers.Control, definition!.Modifiers);
    }

    [Fact]
    public void 修飾鍵數值對齊Win32常數()
    {
        // RegisterHotKey 直接吃這些值，對不上就註冊不起來。
        Assert.Equal(0x0001, (int)HotkeyModifiers.Alt);
        Assert.Equal(0x0002, (int)HotkeyModifiers.Control);
        Assert.Equal(0x0004, (int)HotkeyModifiers.Shift);
        Assert.Equal(0x0008, (int)HotkeyModifiers.Win);
    }
}
