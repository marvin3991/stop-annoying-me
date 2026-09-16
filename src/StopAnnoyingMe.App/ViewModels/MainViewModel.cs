using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StopAnnoyingMe.Core.Models;

namespace StopAnnoyingMe.App.ViewModels;

/// <summary>清單中的一筆記錄。點一下可以開啟編輯畫面改來源與備註。</summary>
internal sealed class RecordItemViewModel(Interruption item)
{
    public long Id { get; } = item.Id;

    public string TimeText { get; } = item.OccurredAt.ToString("HH:mm:ss");

    public string SourceText { get; } = item.Source ?? "—";

    /// <summary>備註內容，沒有備註時為空字串（清單上就不會顯示任何東西）。</summary>
    public string NoteText { get; } = item.Note ?? string.Empty;

    public string Tooltip { get; } = string.IsNullOrWhiteSpace(item.Note)
        ? "點一下可以編輯來源與備註"
        : $"備註：{item.Note}\n點一下可以編輯";

    /// <summary>
    /// 螢幕報讀軟體會讀 ListBoxItem 的名稱，預設會變成類別名稱，
    /// 這裡給它一個有意義的字串。
    /// </summary>
    public override string ToString() =>
        string.IsNullOrWhiteSpace(NoteText)
            ? $"{TimeText}　來源 {SourceText}"
            : $"{TimeText}　來源 {SourceText}　備註 {NoteText}";
}

/// <summary>標籤列上的一顆 chip。選取狀態透過 Tag 傳給主題樣式。</summary>
internal sealed class SourceChipViewModel(string name) : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; } = name;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TagValue));
        }
    }

    /// <summary>主題樣式以 Tag="Selected" 判斷選取狀態。</summary>
    public string? TagValue => _isSelected ? "Selected" : null;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class MainViewModel : INotifyPropertyChanged
{
    private int _todayCount;
    private bool _canUndo;
    private bool _alwaysOnTop;
    private string _hotkeyHint = string.Empty;
    private bool _hasRecords;

    public ObservableCollection<RecordItemViewModel> Recent { get; } = [];

    public ObservableCollection<SourceChipViewModel> Chips { get; } = [];

    public int TodayCount
    {
        get => _todayCount;
        set
        {
            if (_todayCount == value)
            {
                return;
            }

            _todayCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TodayCountText));
        }
    }

    public string TodayCountText => _todayCount.ToString();

    public bool CanUndo
    {
        get => _canUndo;
        set => SetField(ref _canUndo, value);
    }

    /// <summary>今天完全沒有記錄時，清單顯示提示文字而不是一片空白。</summary>
    public bool HasRecords
    {
        get => _hasRecords;
        set => SetField(ref _hasRecords, value);
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set => SetField(ref _alwaysOnTop, value);
    }

    public string HotkeyHint
    {
        get => _hotkeyHint;
        set => SetField(ref _hotkeyHint, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
