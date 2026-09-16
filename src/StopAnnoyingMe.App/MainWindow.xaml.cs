using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StopAnnoyingMe.App.Interop;
using StopAnnoyingMe.App.Services;
using StopAnnoyingMe.App.ViewModels;
using StopAnnoyingMe.App.Views;
using StopAnnoyingMe.Core.Models;

namespace StopAnnoyingMe.App;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly TrayIconController _tray;
    private readonly MainViewModel _viewModel = new();
    private readonly GlobalHotkey _hotkey = new();
    private readonly DispatcherTimer _dayWatcher;
    private readonly DispatcherTimer _snackbarTimer;

    private DateOnly _currentDay;
    private bool _exiting;
    private string? _pendingStartupWarning;

    internal MainWindow(AppServices services, TrayIconController tray, string? startupWarning)
    {
        _services = services;
        _tray = tray;
        _pendingStartupWarning = startupWarning;

        InitializeComponent();
        DataContext = _viewModel;

        _currentDay = _services.Interruptions.Today();

        _snackbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _snackbarTimer.Tick += (_, _) =>
        {
            _snackbarTimer.Stop();
            Snackbar.Visibility = Visibility.Collapsed;
        };

        // 每 30 秒檢查一次日期，跨過午夜時今日次數才會歸零。
        _dayWatcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _dayWatcher.Tick += OnDayWatcherTick;
        _dayWatcher.Start();

        WireTray();
        BuildChips();
        ApplyAlwaysOnTop(_services.Settings.AlwaysOnTop);
        RestoreWindowPosition();
        Refresh();
    }

    /// <summary>把視窗叫回前景，供系統匣與第二次啟動使用。</summary>
    public void BringToFront()
    {
        Show();
        WindowState = WindowState.Normal;

        // 置頂關閉時，單純 Activate 不一定能搶到前景，先短暫置頂再還原。
        var wasTopmost = Topmost;
        Topmost = true;
        Activate();
        Topmost = wasTopmost || _services.Settings.AlwaysOnTop;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        RegisterHotkey();

        if (_pendingStartupWarning is not null)
        {
            ShowMessage(_pendingStartupWarning);
            _pendingStartupWarning = null;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _services.Settings.MinimizeToTray && _tray.IsAvailable)
        {
            // 關閉鈕預設是收到系統匣，不是結束程式。
            e.Cancel = true;
            SaveWindowPosition();
            Hide();
            return;
        }

        SaveWindowPosition();
        _dayWatcher.Stop();
        _hotkey.Dispose();
        base.OnClosing(e);

        Application.Current.Shutdown();
    }

    // ── 記錄 ────────────────────────────────────────────────────────────

    private void OnRecordClick(object sender, RoutedEventArgs e) => RecordInterruption(fromHotkey: false);

    private void RecordInterruption(bool fromHotkey)
    {
        try
        {
            var result = _services.Interruptions.Record(_services.Settings.DuplicateGuardSeconds);

            if (!result.Recorded)
            {
                var text = $"{_services.Settings.DuplicateGuardSeconds} 秒內只記一次，這次沒有重複記錄。";
                ShowMessage(text);
                if (fromHotkey)
                {
                    ToastWindow.Show("剛剛已經記過了", text, ToastWindow.Tone.Warning);
                }

                return;
            }

            Refresh();

            if (fromHotkey)
            {
                ToastWindow.Show(
                    $"今日第 {result.TodayCount} 次",
                    result.Item!.OccurredAt.ToString("HH:mm:ss"));
            }
        }
        catch (Exception ex)
        {
            HandleDataError("記錄失敗", ex);
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _services.Interruptions.UndoLastToday();
            if (!result.Undone)
            {
                ShowMessage("今天沒有可以撤銷的記錄。");
                return;
            }

            Refresh();
            ShowMessage($"已撤銷 {result.Removed!.OccurredAt:HH:mm:ss} 那一筆。");
        }
        catch (Exception ex)
        {
            HandleDataError("撤銷失敗", ex);
        }
    }

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SourceChipViewModel chip })
        {
            return;
        }

        try
        {
            // 再點一次同一個標籤就取消標記。
            var source = chip.IsSelected ? null : chip.Name;

            if (!_services.Interruptions.TagLastToday(source))
            {
                ShowMessage("今天還沒有記錄，先按「＋1 被打擾」再標記來源。");
                return;
            }

            Refresh();
        }
        catch (Exception ex)
        {
            HandleDataError("標記來源失敗", ex);
        }
    }

    // ── 畫面更新 ────────────────────────────────────────────────────────

    private void Refresh()
    {
        try
        {
            var recent = _services.Interruptions.RecentToday(5);

            _viewModel.TodayCount = _services.Interruptions.TodayCount();
            _viewModel.Recent.Clear();
            foreach (var item in recent)
            {
                _viewModel.Recent.Add(new RecordItemViewModel(item));
            }

            _viewModel.HasRecords = recent.Count > 0;
            _viewModel.CanUndo = recent.Count > 0;
            EmptyHint.Visibility = recent.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

            var selectedSource = recent.Count > 0 ? recent[0].Source : null;
            foreach (var chip in _viewModel.Chips)
            {
                chip.IsSelected = chip.Name == selectedSource;
            }

            _tray.UpdateCount(_viewModel.TodayCount);
        }
        catch (Exception ex)
        {
            HandleDataError("讀取記錄失敗", ex);
        }
    }

    private void BuildChips()
    {
        _viewModel.Chips.Clear();
        foreach (var source in _services.Settings.Sources)
        {
            _viewModel.Chips.Add(new SourceChipViewModel(source));
        }
    }

    private void OnDayWatcherTick(object? sender, EventArgs e)
    {
        var today = _services.Interruptions.Today();
        if (today == _currentDay)
        {
            return;
        }

        _currentDay = today;
        Refresh();
    }

    // ── 熱鍵 ────────────────────────────────────────────────────────────

    private void RegisterHotkey()
    {
        _hotkey.Pressed -= OnHotkeyPressed;
        _hotkey.Unregister();

        if (!_services.Settings.HotkeyEnabled)
        {
            _viewModel.HotkeyHint = "熱鍵已關閉";
            return;
        }

        if (!HotkeyDefinition.TryParse(_services.Settings.Hotkey, out var definition))
        {
            _viewModel.HotkeyHint = "熱鍵設定無效";
            ShowMessage($"熱鍵「{_services.Settings.Hotkey}」格式不正確，請到設定重新指定。");
            return;
        }

        if (_hotkey.TryRegister(this, definition, out var error))
        {
            _hotkey.Pressed += OnHotkeyPressed;
            _viewModel.HotkeyHint = definition.ToString();
            return;
        }

        _viewModel.HotkeyHint = "熱鍵未啟用";
        ShowMessage(error ?? "熱鍵註冊失敗。");
    }

    private void OnHotkeyPressed() => RecordInterruption(fromHotkey: true);

    // ── 系統匣與視窗 ────────────────────────────────────────────────────

    private void WireTray()
    {
        _tray.RecordRequested += () => RecordInterruption(fromHotkey: true);
        _tray.ShowRequested += BringToFront;
        _tray.StatsRequested += OpenStats;
        _tray.SettingsRequested += OpenSettings;
        _tray.ExitRequested += ExitApplication;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnStatsClick(object sender, RoutedEventArgs e) => OpenStats();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenStats()
    {
        BringToFront();
        new StatsWindow(_services) { Owner = this }.ShowDialog();
    }

    private void OpenSettings()
    {
        BringToFront();

        var dialog = new SettingsWindow(_services) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        BuildChips();
        ApplyAlwaysOnTop(_services.Settings.AlwaysOnTop);
        RegisterHotkey();
        Refresh();
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    private void OnTogglePinClick(object sender, RoutedEventArgs e)
    {
        ApplyAlwaysOnTop(!_services.Settings.AlwaysOnTop);
        _services.Settings.AlwaysOnTop = Topmost;
        TrySaveSettings();
    }

    private void ApplyAlwaysOnTop(bool enabled)
    {
        Topmost = enabled;
        _viewModel.AlwaysOnTop = enabled;
        _services.Settings.AlwaysOnTop = enabled;

        if (enabled && Application.Current.TryFindResource("Brush.Accent") is Brush accent)
        {
            PinButton.Foreground = accent;
        }
        else
        {
            // 清掉本地值，讓樣式裡的預設前景色重新生效。
            PinButton.ClearValue(ForegroundProperty);
        }

        PinButton.ToolTip = enabled ? "取消視窗置頂" : "視窗置頂";
    }

    private void RestoreWindowPosition()
    {
        var workArea = SystemParameters.WorkArea;
        var left = _services.Settings.WindowLeft;
        var top = _services.Settings.WindowTop;

        // 螢幕數量或解析度變了，舊位置可能落在看不到的地方，超出範圍就回到預設位置。
        var isVisible = left is not null && top is not null
            && left.Value >= workArea.Left - 50
            && top.Value >= workArea.Top - 50
            && left.Value + 100 <= workArea.Right
            && top.Value + 100 <= workArea.Bottom;

        if (isVisible)
        {
            Left = left!.Value;
            Top = top!.Value;
            return;
        }

        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;
    }

    private void SaveWindowPosition()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        _services.Settings.WindowLeft = Left;
        _services.Settings.WindowTop = Top;
        TrySaveSettings();
    }

    private void TrySaveSettings()
    {
        try
        {
            _services.SaveSettings();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("儲存設定失敗", ex);
            ShowMessage("設定儲存失敗，這次的變更可能不會保留。");
        }
    }

    // ── 提示訊息 ────────────────────────────────────────────────────────

    private void ShowMessage(string text)
    {
        SnackbarText.Text = text;
        Snackbar.Visibility = Visibility.Visible;

        _snackbarTimer.Stop();
        _snackbarTimer.Start();
    }

    private void HandleDataError(string context, Exception exception)
    {
        ErrorLog.Write(context, exception);
        ShowMessage($"{context}：{exception.Message}");
    }
}
