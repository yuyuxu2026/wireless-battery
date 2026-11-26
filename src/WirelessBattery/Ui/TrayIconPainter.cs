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
        FillRounded(graphics, brush, new RectangleF(1.5f, 8, 24, 16), 3);
        graphics.FillRectangle(brush, 26.5f, 12, 3.5f, 8);

        if (percent is int value)
        {
            var size = value >= 100 ? 8f : 11f;
            using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString(value.ToString(), font, textBrush, new RectangleF(1.5f, 8, 24, 16), format);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static void FillRounded(Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
