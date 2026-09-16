using System.Diagnostics.CodeAnalysis;

namespace StopAnnoyingMe.Core.Models;

/// <summary>修飾鍵。數值刻意對齊 Win32 RegisterHotKey 的 MOD_* 常數，可直接傳給 API。</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>
/// 全域熱鍵定義，例如 "Ctrl+Alt+D"。
/// 解析與驗證放在 Core，UI 只負責把結果丟給 Win32 API，這樣規則本身可以被測試。
/// </summary>
public sealed record HotkeyDefinition(HotkeyModifiers Modifiers, string Key)
{
    /// <summary>
    /// 解析熱鍵字串。必須至少有一個修飾鍵，否則會霸佔一般打字用的按鍵。
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyDefinition? definition)
    {
        definition = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? mainKey = null;

        foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN" or "WINDOWS":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    // 只允許一個主鍵
                    if (mainKey is not null)
                    {
                        return false;
                    }

                    mainKey = rawPart.ToUpperInvariant();
                    break;
            }
        }

        if (mainKey is null || modifiers == HotkeyModifiers.None || ToVirtualKey(mainKey) is null)
        {
            return false;
        }

        definition = new HotkeyDefinition(modifiers, mainKey);
        return true;
    }

    /// <summary>Win32 虛擬鍵碼。建構時已驗證過，這裡必定有值。</summary>
    public int VirtualKeyCode => ToVirtualKey(Key)
        ?? throw new InvalidOperationException($"不支援的按鍵：{Key}");

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }

    /// <summary>支援的主鍵：英數字、F1-F24 與幾個常用的編輯／導覽鍵。</summary>
    private static int? ToVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            var c = key[0];
            if (c is >= 'A' and <= 'Z') return 0x41 + (c - 'A');
            if (c is >= '0' and <= '9') return 0x30 + (c - '0');
            return null;
        }

        if (key.Length is 2 or 3 && key[0] == 'F'
            && int.TryParse(key[1..], out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            return 0x70 + (functionNumber - 1);
        }

        return key switch
        {
            "SPACE" => 0x20,
            "INSERT" or "INS" => 0x2D,
            "DELETE" or "DEL" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "BACKSPACE" => 0x08,
            "TAB" => 0x09,
            "ENTER" or "RETURN" => 0x0D,
            _ => null,
        };
    }
}
