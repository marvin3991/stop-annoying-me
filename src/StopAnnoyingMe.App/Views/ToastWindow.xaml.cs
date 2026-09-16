using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace StopAnnoyingMe.App.Views;

/// <summary>
/// 螢幕右下角的短暫提示。用自己的視窗而不是系統匣氣泡，
/// 因為 Windows 的氣泡會被「專注輔助」直接吃掉，
/// 而熱鍵記錄後的回饋一定要看得到。
/// </summary>
public partial class ToastWindow : Window
{
    /// <summary>提示語氣，對應主題裡的強調色／警告色／錯誤色。</summary>
    public enum Tone
    {
        Accent,
        Warning,
        Danger,
    }

    private const double ScreenMargin = 16;

    private static ToastWindow? _instance;

    private readonly DispatcherTimer _hideTimer;

    private ToastWindow()
    {
        InitializeComponent();

        // 2.5 秒足夠看清楚「今日第幾次」，又不會擋住畫面太久。
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    /// <summary>顯示提示。重複呼叫會重用同一個視窗並重新計時。</summary>
    public static void Show(string title, string body, Tone tone = Tone.Accent)
    {
        _instance ??= new ToastWindow();
        _instance.Present(title, body, tone);
    }

    /// <summary>程式結束時關閉重用中的視窗，否則 WPF 會等不到關閉。</summary>
    public static void Shutdown()
    {
        _instance?.Close();
        _instance = null;
    }

    private void Present(string title, string body, Tone tone)
    {
        _hideTimer.Stop();

        TitleText.Text = title;
        BodyText.Text = body;
        BodyText.Visibility = string.IsNullOrEmpty(body) ? Visibility.Collapsed : Visibility.Visible;

        var brushKey = tone switch
        {
            Tone.Warning => "Brush.Warning",
            Tone.Danger => "Brush.Danger",
            _ => "Brush.Accent",
        };

        if (Application.Current.TryFindResource(brushKey) is Brush brush)
        {
            Frame.BorderBrush = brush;
        }

        // 先量測才能算出右下角的正確位置
        Opacity = 0;
        base.Show();
        UpdateLayout();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - ScreenMargin;
        Top = workArea.Bottom - ActualHeight - ScreenMargin;
        Opacity = 1;

        _hideTimer.Start();
    }
}
