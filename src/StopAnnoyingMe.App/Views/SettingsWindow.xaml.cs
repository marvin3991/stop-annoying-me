using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using StopAnnoyingMe.App.Services;
using StopAnnoyingMe.Core.Models;

namespace StopAnnoyingMe.App.Views;

public partial class SettingsWindow : Window
{
    private static readonly char[] SourceSeparators = [',', '，', '、', ';', '；'];

    private readonly AppServices _services;

    internal SettingsWindow(AppServices services)
    {
        _services = services;

        InitializeComponent();
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var settings = _services.Settings;

        HotkeyEnabledCheck.IsChecked = settings.HotkeyEnabled;
        HotkeyBox.Text = settings.Hotkey;
        AlwaysOnTopCheck.IsChecked = settings.AlwaysOnTop;
        MinimizeToTrayCheck.IsChecked = settings.MinimizeToTray;
        // 以登錄檔的實際狀態為準，使用者可能在外面手動改過。
        RunAtStartupCheck.IsChecked = StartupManager.IsEnabled();
        GuardSecondsBox.Text = settings.DuplicateGuardSeconds.ToString(CultureInfo.InvariantCulture);
        SourcesBox.Text = string.Join("，", settings.Sources);
        DataPathText.Text = _services.Paths.RootDirectory;
    }

    /// <summary>直接按下組合鍵來設定熱鍵，比手動輸入字串不容易出錯。</summary>
    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Alt 組合會以 System 形式送來，真正的鍵在 SystemKey。
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (IsModifierKey(key))
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            ShowError("熱鍵必須包含 Ctrl、Alt、Shift 或 Win 其中至少一個。");
            return;
        }

        var keyName = ToKeyName(key);
        if (keyName is null)
        {
            ShowError("這個按鍵不支援，請改用英文字母、數字或 F1–F24。");
            return;
        }

        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(keyName);

        var candidate = string.Join("+", parts);
        if (!HotkeyDefinition.TryParse(candidate, out var definition))
        {
            ShowError($"「{candidate}」不是有效的熱鍵組合。");
            return;
        }

        HotkeyBox.Text = definition.ToString();
        ClearError();
    }

    private static bool IsModifierKey(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin
        or Key.System;

    private static string? ToKeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(CultureInfo.InvariantCulture),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Space => "Space",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Back => "Backspace",
        Key.Tab => "Tab",
        Key.Enter => "Enter",
        _ => null,
    };

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var hotkeyEnabled = HotkeyEnabledCheck.IsChecked == true;
        var hotkeyText = HotkeyBox.Text.Trim();

        if (hotkeyEnabled && !HotkeyDefinition.TryParse(hotkeyText, out _))
        {
            ShowError("熱鍵格式不正確，請點欄位後重新按一次組合鍵。");
            return;
        }

        if (!int.TryParse(GuardSecondsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var guardSeconds)
            || guardSeconds is < 0 or > 3600)
        {
            ShowError("手滑保護秒數必須是 0 到 3600 之間的整數。");
            return;
        }

        var sources = SourcesBox.Text
            .Split(SourceSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();

        if (sources.Count == 0)
        {
            ShowError("至少要留一個標籤，或還原成預設值。");
            return;
        }

        // 登錄檔可能寫不進去，先確認成功再套用其餘設定。
        var runAtStartup = RunAtStartupCheck.IsChecked == true;
        if (runAtStartup != StartupManager.IsEnabled())
        {
            try
            {
                StartupManager.SetEnabled(runAtStartup);
            }
            catch (InvalidOperationException ex)
            {
                ErrorLog.Write("設定開機自動啟動失敗", ex);
                RunAtStartupCheck.IsChecked = StartupManager.IsEnabled();
                ShowError(ex.Message);
                return;
            }
        }

        var settings = _services.Settings;
        settings.HotkeyEnabled = hotkeyEnabled;
        settings.Hotkey = string.IsNullOrWhiteSpace(hotkeyText) ? settings.Hotkey : hotkeyText;
        settings.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        settings.RunAtStartup = runAtStartup;
        settings.DuplicateGuardSeconds = guardSeconds;
        settings.Sources = sources;

        try
        {
            _services.SaveSettings();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("儲存設定失敗", ex);
            ShowError($"設定儲存失敗：{ex.Message}");
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _services.Paths.RootDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ErrorLog.Write("開啟資料夾失敗", ex);
            ShowError($"無法開啟資料夾：{ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
