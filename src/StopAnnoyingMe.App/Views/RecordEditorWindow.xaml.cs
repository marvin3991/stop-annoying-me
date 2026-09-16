using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StopAnnoyingMe.App.Services;
using StopAnnoyingMe.App.ViewModels;
using StopAnnoyingMe.Core.Models;

namespace StopAnnoyingMe.App.Views;

/// <summary>
/// 編輯單一筆記錄的來源與備註。發生時間刻意不開放修改 ——
/// 那是這個工具唯一的事實依據，能改就失去意義了。
/// </summary>
public partial class RecordEditorWindow : Window
{
    private static readonly string[] WeekdayNames = ["日", "一", "二", "三", "四", "五", "六"];

    private readonly AppServices _services;
    private readonly Interruption _item;
    private readonly ObservableCollection<SourceChipViewModel> _chips = [];

    private string? _selectedSource;

    /// <summary>
    /// 對話框是以「刪除」而不是「儲存」結束的。
    /// 呼叫端用這個決定要顯示哪一種提示訊息。
    /// </summary>
    public bool Deleted { get; private set; }

    internal RecordEditorWindow(AppServices services, Interruption item)
    {
        _services = services;
        _item = item;

        InitializeComponent();

        TimeText.Text = item.OccurredAt.ToString("HH:mm:ss");
        DateText.Text = $"{item.OccurredAt:yyyy-MM-dd}（週{WeekdayNames[(int)item.OccurredAt.DayOfWeek]}）";

        _selectedSource = item.Source;

        foreach (var name in services.Settings.Sources)
        {
            _chips.Add(new SourceChipViewModel(name) { IsSelected = name == item.Source });
        }

        // 設定裡的標籤清單改過之後，舊記錄的來源可能已不在清單中，
        // 補一顆進去，否則使用者會看不到目前的值、一存檔就被清掉。
        if (!string.IsNullOrWhiteSpace(item.Source)
            && !services.Settings.Sources.Contains(item.Source, StringComparer.Ordinal))
        {
            _chips.Add(new SourceChipViewModel(item.Source) { IsSelected = true });
        }

        ChipList.ItemsSource = _chips;
        NoteBox.Text = item.Note ?? string.Empty;

        Loaded += (_, _) =>
        {
            NoteBox.Focus();
            NoteBox.CaretIndex = NoteBox.Text.Length;
        };
    }

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SourceChipViewModel chip })
        {
            return;
        }

        // 點已選取的那顆等於取消選取，來源就變成未指定。
        _selectedSource = chip.IsSelected ? null : chip.Name;

        foreach (var item in _chips)
        {
            item.IsSelected = item.Name == _selectedSource;
        }

        ClearError();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var note = NoteBox.Text.Trim();

        try
        {
            if (!_services.Interruptions.UpdateDetails(_item.Id, _selectedSource, note))
            {
                ShowError("這筆記錄已經不存在，可能剛剛被撤銷了。");
                return;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("儲存記錄編輯失敗", ex);
            ShowError($"儲存失敗：{ex.Message}");
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

    /// <summary>第一步：切到確認狀態。這一步不會動到任何資料。</summary>
    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        ClearError();
        NormalActions.Visibility = Visibility.Collapsed;
        DeleteConfirm.Visibility = Visibility.Visible;

        // 焦點移到確認鈕，鍵盤使用者不用再 Tab 一次
        DeleteConfirmButton.Focus();
    }

    private void OnDeleteCancelClick(object sender, RoutedEventArgs e) => ReturnToNormalActions();

    /// <summary>第二步：真的刪除。這個動作無法復原。</summary>
    private void OnDeleteConfirmClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 回傳 false 代表這筆已經不在了（例如在主畫面按過撤銷），
            // 結果與使用者要的一致，照樣當成刪除完成讓主畫面重新整理。
            _services.Interruptions.Delete(_item.Id);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("刪除記錄失敗", ex);
            ReturnToNormalActions();
            ShowError($"刪除失敗：{ex.Message}");
            return;
        }

        Deleted = true;
        DialogResult = true;
        Close();
    }

    private void ReturnToNormalActions()
    {
        DeleteConfirm.Visibility = Visibility.Collapsed;
        NormalActions.Visibility = Visibility.Visible;
    }

    /// <summary>在確認刪除的狀態下按 Esc，先退回一般狀態而不是直接關掉視窗。</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DeleteConfirm.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            ReturnToNormalActions();
            return;
        }

        base.OnPreviewKeyDown(e);
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
