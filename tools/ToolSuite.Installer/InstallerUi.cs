using System.Drawing.Drawing2D;

namespace ToolSuite.Installer;

/// <summary>安装程序的颜色（docs/design-system.md 第 4 节：安装程序）。
/// 安装程序是独立的一次性对话框，只使用亮色一套，不与宿主主题联动。</summary>
internal static class Palette
{
    public static readonly Color Canvas = Color.FromArgb(0xF5, 0xF5, 0xF7);
    public static readonly Color Card = Color.FromArgb(0xFF, 0xFF, 0xFF);
    public static readonly Color Well = Color.FromArgb(0xF5, 0xF5, 0xF7);
    public static readonly Color Hover = Color.FromArgb(0xE8, 0xE8, 0xED);
    public static readonly Color Ink = Color.FromArgb(0x1D, 0x1D, 0x1F);
    public static readonly Color InkSecondary = Color.FromArgb(0x63, 0x63, 0x6A);
    public static readonly Color InkTertiary = Color.FromArgb(0x75, 0x75, 0x7A);
    public static readonly Color Hairline = Color.FromArgb(0xD6, 0xD6, 0xD6);
    public static readonly Color Strong = Color.FromArgb(0xB8, 0xB8, 0xBD);
    public static readonly Color Accent = Color.FromArgb(0x00, 0x71, 0xE3);
    public static readonly Color AccentHover = Color.FromArgb(0x00, 0x62, 0xC4);
    public static readonly Color Danger = Color.FromArgb(0xC7, 0x26, 0x1F);
    public static readonly Color FocusRing = Color.FromArgb(0x29, 0x00, 0x71, 0xE3);

    public const string Family = "Segoe UI Variable Text";

    /// <summary>字重上限 600：优先可变字体的 Semibold 家族，退回系统 SemiBold / Bold。</summary>
    public static Font Title(float size) => SemiBold(size) ?? new Font(Family, size, FontStyle.Bold);

    public static Font SemiBold(float size)
    {
        foreach (var name in new[] { "Segoe UI Variable Display Semibold", "Segoe UI Semibold" })
        {
            var family = FontFamily.Families.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (family is not null) return new Font(family, size);
        }
        return new Font(Family, size, FontStyle.Bold);
    }

    public static Font Text(float size) => new(Family, size);
}

/// <summary>圆角辅助：半径超过短边一半时收敛为半圆（胶囊），只做四角圆弧。</summary>
internal static class Rounded
{
    public static GraphicsPath Path(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var limit = Math.Min(bounds.Width, bounds.Height) / 2;
        var r = Math.Clamp(radius, 0, Math.Max(0, limit));
        var d = r * 2;
        // 半径正好等于半高（胶囊）时两端各是两个 90° 圆弧，仍然能正常画，
        // 所以这里用严格小于判断，否则胶囊会退化成直角矩形。
        if (d <= 0 || bounds.Width < d || bounds.Height < d)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>安装对话框的自绘原语。所有图元都画在同一张表面上，不依赖子控件重绘，
/// 因此不存在「控件之间互相覆盖/残留」的窗口合成问题。</summary>
internal static class Chrome
{
    public static void Card(Graphics g, Rectangle bounds, int radius, Color fill, Color stroke)
    {
        using var path = Rounded.Path(bounds, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        if (stroke != Color.Empty)
        {
            using var pen = new Pen(stroke, 1f);
            g.DrawPath(pen, path);
        }
    }

    public static void Pill(Graphics g, Rectangle bounds, Color fill, Color stroke, string text,
        Font font, Color textColor, float strokeWidth = 1f)
    {
        using var path = Rounded.Path(bounds, bounds.Height / 2);
        if (fill != Color.Empty)
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }
        if (stroke != Color.Empty)
        {
            using var pen = new Pen(stroke, strokeWidth);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, text, font, bounds, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    public static void Check(Graphics g, Rectangle box, bool isChecked, bool hovered, Color border)
    {
        using var path = Rounded.Path(box, Math.Max(2, box.Width / 4));
        if (isChecked)
        {
            using var brush = new SolidBrush(Palette.Accent);
            g.FillPath(brush, path);
            using var pen = new Pen(Color.White, Math.Max(1.6f, box.Width * 0.11f))
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
            };
            g.DrawLines(pen,
            [
                new PointF(box.Left + box.Width * 0.20f, box.Top + box.Height * 0.52f),
                new PointF(box.Left + box.Width * 0.42f, box.Top + box.Height * 0.73f),
                new PointF(box.Left + box.Width * 0.79f, box.Top + box.Height * 0.30f)
            ]);
        }
        else
        {
            using var brush = new SolidBrush(Color.White);
            g.FillPath(brush, path);
            using var pen = new Pen(hovered ? Palette.InkSecondary : border, 1f);
            g.DrawPath(pen, path);
        }
    }

    public static void Text(Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags flags)
        => TextRenderer.DrawText(g, text, font, bounds, color, flags | TextFormatFlags.NoPadding);
}
