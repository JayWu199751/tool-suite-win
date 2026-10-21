using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ToolSuite.Installer;

/// <summary>安装程序的控件语言（docs/design-system.md 第 4 节：安装程序）。
/// WinForms 没有圆角与胶囊原生支持，这里自绘三件事：圆角卡片、胶囊按钮、方形勾选。
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
        if (d <= 0 || bounds.Width <= d || bounds.Height <= d)
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

/// <summary>卡片与输入井：圆角填充 + 可选发丝描边，零投影。</summary>
internal sealed class CardPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 20;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Palette.Card;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Stroke { get; set; } = Color.Empty;

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Palette.Canvas;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Rounded.Path(bounds, CornerRadius);
        using var fill = new SolidBrush(Fill);
        e.Graphics.FillPath(fill, path);
        if (Stroke != Color.Empty)
        {
            using var pen = new Pen(Stroke, 1f);
            e.Graphics.DrawPath(pen, path);
        }
    }
}

/// <summary>胶囊按钮：高度一半的圆角，主操作用实心蓝，次操作用发丝描边幽灵样式。</summary>
internal sealed class PillButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Palette.Accent;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Stroke { get; set; } = Color.Empty;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TextColor { get; set; } = Color.White;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverFill { get; set; } = Palette.AccentHover;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverStroke { get; set; } = Color.Empty;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color DisabledFill { get; set; } = Palette.Hover;

    public PillButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        BackColor = Palette.Canvas;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs mevent) { base.OnMouseDown(mevent); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs mevent) { base.OnMouseUp(mevent); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var pressed = MouseButtons != MouseButtons.None && ClientRectangle.Contains(PointToClient(MousePosition));
        var hovered = !pressed && ClientRectangle.Contains(PointToClient(MousePosition));
        var fill = !Enabled ? Palette.Hover : hovered ? HoverFill : Fill;
        var stroke = !Enabled ? Color.Empty : hovered && HoverStroke != Color.Empty ? HoverStroke : Stroke;
        var text = !Enabled ? Palette.InkTertiary : TextColor;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Rounded.Path(bounds, Height / 2);
        if (fill != Color.Empty && fill.A > 0)
        {
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);
        }
        if (stroke != Color.Empty)
        {
            using var pen = new Pen(stroke, 1f);
            e.Graphics.DrawPath(pen, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font,
            new Rectangle(0, 0, Width, Height), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>方形勾选：16 DIP 圆角方框，选中填主蓝并画白色勾，未选为发丝描边。</summary>
internal sealed class SuiteCheckBox : CheckBox
{
    private const int BoxSize = 16;
    private const int Gap = 8;

    public SuiteCheckBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 24;
        Font = Palette.Text(9);
        BackColor = Palette.Card;
        ForeColor = Palette.Ink;
        Cursor = Cursors.Hand;
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new Rectangle(0, (Height - BoxSize) / 2, BoxSize, BoxSize);
        using var path = Rounded.Path(box, 4);
        if (Checked)
        {
            using var brush = new SolidBrush(Palette.Accent);
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(Color.White, 1.8f)
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
            };
            e.Graphics.DrawLines(pen,
            [
                new PointF(box.X + 3.4f, box.Y + 8.2f),
                new PointF(box.X + 6.6f, box.Y + 11.4f),
                new PointF(box.X + 12.4f, box.Y + 5.2f)
            ]);
        }
        else
        {
            using var brush = new SolidBrush(Color.White);
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(ClientRectangle.Contains(PointToClient(MousePosition)) ? Palette.InkSecondary : Palette.Strong, 1f);
            e.Graphics.DrawPath(pen, path);
        }
        if (Focused)
        {
            using var ring = new Pen(Color.FromArgb(0x29, 0x00, 0x71, 0xE3), 2f);
            using var ringPath = Rounded.Path(Rectangle.Inflate(box, 3, 3), 7);
            e.Graphics.DrawPath(ring, ringPath);
        }
        var textBounds = new Rectangle(BoxSize + Gap, 0, Math.Max(0, Width - BoxSize - Gap), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds,
            Enabled ? ForeColor : Palette.InkTertiary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}
