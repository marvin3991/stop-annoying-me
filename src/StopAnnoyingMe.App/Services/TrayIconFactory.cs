using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace StopAnnoyingMe.App.Services;

/// <summary>
/// 產生系統匣圖示。圖示上直接畫出今日次數，
/// 讓使用者不用開視窗就能看到數字。
/// </summary>
internal static class TrayIconFactory
{
    private const int IconSize = 32;

    private static readonly Color Background = ColorTranslator.FromHtml("#4FC3B6");
    private static readonly Color Foreground = ColorTranslator.FromHtml("#071513");

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>建立標示次數的圖示。呼叫端負責 Dispose。</summary>
    public static Icon CreateCountIcon(int count)
    {
        var text = count switch
        {
            <= 0 => "0",
            > 99 => "99",     // 三位數在 16x16 會糊掉，超過 99 一律顯示 99
            _ => count.ToString(),
        };

        // 位數越多字要越小才塞得下
        var fontSize = text.Length switch
        {
            1 => 22f,
            2 => 17f,
            _ => 13f,
        };

        using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            using (var background = new SolidBrush(Background))
            using (var shape = CreateRoundedRectangle(new Rectangle(0, 0, IconSize - 1, IconSize - 1), 8))
            {
                graphics.FillPath(background, shape);
            }

            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Foreground);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            graphics.DrawString(text, font, textBrush, new RectangleF(0, 0, IconSize, IconSize), format);
        }

        // GetHicon 產生的是未受管理的控制代碼，Clone 出獨立副本後必須立刻銷毀，否則會漏 GDI 物件。
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
