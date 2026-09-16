using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using StopAnnoyingMe.App.Services;
using StopAnnoyingMe.App.ViewModels;
using StopAnnoyingMe.Core.Services;

namespace StopAnnoyingMe.App.Views;

public partial class StatsWindow : Window
{
    private readonly AppServices _services;
    private readonly StatsViewModel _viewModel = new();

    internal StatsWindow(AppServices services)
    {
        _services = services;

        InitializeComponent();
        DataContext = _viewModel;

        LoadSummary();
        SelectPeriod(StatsPeriod.Today);
    }

    private void LoadSummary()
    {
        var today = _services.Interruptions.Today();
        var summary = _services.Statistics.Summarize(today);

        _viewModel.TodayText = summary.Today.ToString();
        _viewModel.WeekText = summary.ThisWeek.ToString();
        _viewModel.MonthText = summary.ThisMonth.ToString();
        _viewModel.TotalText = summary.Total.ToString();
    }

    private void OnPeriodClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var period = button.Name switch
        {
            nameof(WeekChip) => StatsPeriod.Week,
            nameof(MonthChip) => StatsPeriod.Month,
            _ => StatsPeriod.Today,
        };

        SelectPeriod(period);
    }

    private void SelectPeriod(StatsPeriod period)
    {
        _viewModel.Period = period;

        TodayChip.Tag = period == StatsPeriod.Today ? "Selected" : null;
        WeekChip.Tag = period == StatsPeriod.Week ? "Selected" : null;
        MonthChip.Tag = period == StatsPeriod.Month ? "Selected" : null;

        var (from, to) = CurrentRange();
        _viewModel.RangeText = from == to
            ? from.ToString("yyyy-MM-dd")
            : $"{from:yyyy-MM-dd} ～ {to:yyyy-MM-dd}";

        try
        {
            _viewModel.SetHourly(_services.Statistics.HourlyDistribution(from, to));
        }
        catch (Exception ex)
        {
            ErrorLog.Write("讀取時段分佈失敗", ex);
            MessageBox.Show(this, $"讀取統計資料失敗：{ex.Message}", "統計", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private (DateOnly From, DateOnly To) CurrentRange()
    {
        var today = _services.Interruptions.Today();

        return _viewModel.Period switch
        {
            StatsPeriod.Week => StatisticsService.WeekRange(today),
            StatsPeriod.Month => StatisticsService.MonthRange(today),
            _ => (today, today),
        };
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var (from, to) = CurrentRange();

        var dialog = new SaveFileDialog
        {
            Title = "匯出打擾記錄",
            Filter = "CSV 檔案 (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = from == to
                ? $"打擾記錄_{from:yyyyMMdd}.csv"
                : $"打擾記錄_{from:yyyyMMdd}-{to:yyyyMMdd}.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var items = _services.Repository.GetRange(from, to);
            var rows = CsvExporter.Export(items, dialog.FileName);

            MessageBox.Show(
                this,
                rows == 0
                    ? $"這段期間沒有記錄，已建立只含標題列的檔案：\n{dialog.FileName}"
                    : $"已匯出 {rows} 筆記錄到：\n{dialog.FileName}",
                "匯出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("匯出 CSV 失敗", ex);
            MessageBox.Show(this, ex.Message, "匯出失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
            MessageBox.Show(this, $"無法開啟資料夾：{ex.Message}", "統計", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
