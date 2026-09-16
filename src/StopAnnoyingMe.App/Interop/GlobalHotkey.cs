using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using StopAnnoyingMe.Core.Models;

namespace StopAnnoyingMe.App.Interop;

/// <summary>
/// 全域熱鍵。註冊失敗（多半是被其他程式佔用）不丟例外，
/// 而是回傳看得懂的訊息讓 UI 提示使用者改鍵，程式本身仍可正常使用。
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x5A11;

    /// <summary>按住不放時不要重複觸發。</summary>
    private const uint ModNoRepeat = 0x4000;

    private const int ErrorHotkeyAlreadyRegistered = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private IntPtr _windowHandle;
    private bool _registered;

    public event Action? Pressed;

    public bool IsRegistered => _registered;

    public HotkeyDefinition? Current { get; private set; }

    /// <summary>
    /// 註冊熱鍵。會先解除舊的註冊，因此可以直接用來切換熱鍵。
    /// </summary>
    /// <param name="window">必須是已經建立視窗控制代碼的視窗（SourceInitialized 之後）。</param>
    public bool TryRegister(Window window, HotkeyDefinition definition, out string? error)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(definition);

        Unregister();

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            error = "視窗尚未初始化，無法註冊熱鍵。";
            return false;
        }

        var source = HwndSource.FromHwnd(handle);
        if (source is null)
        {
            error = "取不到視窗訊息來源，無法註冊熱鍵。";
            return false;
        }

        source.AddHook(OnWindowMessage);

        var modifiers = (uint)definition.Modifiers | ModNoRepeat;
        if (!RegisterHotKey(handle, HotkeyId, modifiers, (uint)definition.VirtualKeyCode))
        {
            var lastError = Marshal.GetLastWin32Error();
            source.RemoveHook(OnWindowMessage);

            error = lastError == ErrorHotkeyAlreadyRegistered
                ? $"熱鍵 {definition} 已被其他程式佔用，請到設定改成別的組合。"
                : $"熱鍵 {definition} 註冊失敗（Windows 錯誤碼 {lastError}）。";
            return false;
        }

        _source = source;
        _windowHandle = handle;
        _registered = true;
        Current = definition;
        error = null;
        return true;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        UnregisterHotKey(_windowHandle, HotkeyId);
        _source?.RemoveHook(OnWindowMessage);

        _source = null;
        _windowHandle = IntPtr.Zero;
        _registered = false;
        Current = null;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose() => Unregister();
}
