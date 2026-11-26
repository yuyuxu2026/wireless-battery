using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace WirelessBattery.Ui;

public static class TrayIconPainter
{
    public static Icon Create(int? percent)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var color = percent switch
        {
            null => Color.FromArgb(120, 116, 108),
            <= 10 => Color.FromArgb(194, 65, 45),
            <= 20 => Color.FromArgb(196, 123, 18),
            _ => Color.FromArgb(31, 107, 69)
        };
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 1, 1, 30, 30);

        if (percent is int value)
        {
            var size = value >= 100 ? 8f : 12f;
            using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString(value.ToString(), font, textBrush, new RectangleF(0, 1, 32, 30), format);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
