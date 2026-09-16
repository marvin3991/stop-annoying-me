using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
