using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StopAnnoyingMe.App.ViewModels;

internal enum StatsPeriod
{
    Today,
    Week,
    Month,
}

/// <summary>時段分佈圖裡的一根長條。高度已經換算成像素，畫面不必再做計算。</summary>
internal sealed class HourBarViewModel
{
    public required int Hour { get; init; }

    public required int Count { get; init; }

    public required double BarHeight { get; init; }

    /// <summary>橫軸刻度，每 3 小時標一次，其餘為空字串。</summary>
    public required string AxisLabel { get; init; }

    public string Tooltip => $"{Hour:00}:00 – {Hour:00}:59　{Count} 次";
}

internal sealed class StatsViewModel : INotifyPropertyChanged
{
    /// <summary>長條圖的最大像素高度。</summary>
    public const double ChartHeight = 140;

    private string _todayText = "0";
    private string _weekText = "0";
    private string _monthText = "0";
    private string _totalText = "0";
    private string _rangeText = string.Empty;
    private string _peakText = string.Empty;
    private StatsPeriod _period = StatsPeriod.Today;

    public ObservableCollection<HourBarViewModel> Hours { get; } = [];

    public string TodayText { get => _todayText; set => SetField(ref _todayText, value); }

    public string WeekText { get => _weekText; set => SetField(ref _weekText, value); }

    public string MonthText { get => _monthText; set => SetField(ref _monthText, value); }

    public string TotalText { get => _totalText; set => SetField(ref _totalText, value); }

    /// <summary>目前檢視的日期範圍說明。</summary>
    public string RangeText { get => _rangeText; set => SetField(ref _rangeText, value); }

    /// <summary>最常被打擾的時段。</summary>
    public string PeakText { get => _peakText; set => SetField(ref _peakText, value); }

    public StatsPeriod Period
    {
        get => _period;
        set => SetField(ref _period, value);
    }

    /// <summary>把每小時次數換算成長條像素高度，零次仍保留一條細線當底。</summary>
    public void SetHourly(int[] counts)
    {
        Hours.Clear();

        var max = counts.Length == 0 ? 0 : counts.Max();

        for (var hour = 0; hour < counts.Length; hour++)
        {
            var count = counts[hour];
            var height = count == 0 || max == 0
                ? 2d
                : Math.Max(4d, ChartHeight * count / max);

            Hours.Add(new HourBarViewModel
            {
                Hour = hour,
                Count = count,
                BarHeight = height,
                AxisLabel = hour % 3 == 0 ? hour.ToString("00") : string.Empty,
            });
        }

        if (max == 0)
        {
            PeakText = "這段期間沒有記錄。";
            return;
        }

        var peakHour = Array.IndexOf(counts, max);
        PeakText = $"最常被打擾：{peakHour:00}:00 – {peakHour:00}:59，共 {max} 次";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
