using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace StopAnnoyingMe.App.Services;

/// <summary>
/// 系統匣圖示與右鍵選單。圖示建立失敗時（例如系統匣不可用）
/// 會靜靜降級成一般視窗模式，不影響記錄功能。
/// </summary>
internal sealed class TrayIconController : IDisposable
{
    private readonly Forms.NotifyIcon? _notifyIcon;
    private Icon? _currentIcon;
    private int _lastCount = -1;

    public TrayIconController()
    {
        try
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Text = "打擾記錄器",
                Visible = true,
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("記一次打擾 (+1)", null, (_, _) => RecordRequested?.Invoke());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("開啟主畫面", null, (_, _) => ShowRequested?.Invoke());
            menu.Items.Add("統計…", null, (_, _) => StatsRequested?.Invoke());
            menu.Items.Add("設定…", null, (_, _) => SettingsRequested?.Invoke());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("結束", null, (_, _) => ExitRequested?.Invoke());

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke();

            UpdateCount(0);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            IsAvailable = false;
        }
    }

    public bool IsAvailable { get; private set; } = true;

    public event Action? RecordRequested;
    public event Action? ShowRequested;
    public event Action? StatsRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public void UpdateCount(int count)
    {
        if (_notifyIcon is null || count == _lastCount)
        {
            return;
        }

        var previous = _currentIcon;
        _currentIcon = TrayIconFactory.CreateCountIcon(count);
        _notifyIcon.Icon = _currentIcon;
        _notifyIcon.Text = $"打擾記錄器 — 今日 {count} 次";
        _lastCount = count;

        previous?.Dispose();
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
        }

        _currentIcon?.Dispose();
    }
}
