namespace Pinto.Core;

public readonly record struct WindowBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public readonly record struct DockingResult(WindowBounds Bounds, DockEdges Edges);

public static class EdgeDocking
{
    public static bool IsValid(DockEdges edges)
        => (edges & ~(DockEdges.Left | DockEdges.Right | DockEdges.Top | DockEdges.Bottom)) == 0
        && (edges & (DockEdges.Left | DockEdges.Right)) != (DockEdges.Left | DockEdges.Right)
        && (edges & (DockEdges.Top | DockEdges.Bottom)) != (DockEdges.Top | DockEdges.Bottom);

    public static WindowBounds Align(WindowBounds bounds, WindowBounds work, DockEdges edges)
    {
        var left = edges.HasFlag(DockEdges.Left) ? work.Left : edges.HasFlag(DockEdges.Right) ? work.Right - bounds.Width : bounds.Left;
        var top = edges.HasFlag(DockEdges.Top) ? work.Top : edges.HasFlag(DockEdges.Bottom) ? work.Bottom - bounds.Height : bounds.Top;
        return bounds with { Left = left, Top = top };
    }

    public static DockingResult Snap(WindowBounds bounds, WindowBounds work, double threshold, bool resizing = false, WindowBounds? previousBounds = null)
    {
        var left = Math.Abs(bounds.Left - work.Left);
        var right = Math.Abs(bounds.Right - work.Right);
        var top = Math.Abs(bounds.Top - work.Top);
        var bottom = Math.Abs(bounds.Bottom - work.Bottom);
        if (!resizing && previousBounds is { } previous)
        {
            if (bounds.Left >= work.Left && !ShouldSnap(left, threshold, Math.Abs(previous.Left - work.Left))) left = double.PositiveInfinity;
            if (bounds.Right <= work.Right && !ShouldSnap(right, threshold, Math.Abs(previous.Right - work.Right))) right = double.PositiveInfinity;
            if (bounds.Top >= work.Top && !ShouldSnap(top, threshold, Math.Abs(previous.Top - work.Top))) top = double.PositiveInfinity;
            if (bounds.Bottom <= work.Bottom && !ShouldSnap(bottom, threshold, Math.Abs(previous.Bottom - work.Bottom))) bottom = double.PositiveInfinity;
        }
        var edges = DockEdges.None;
        if (Math.Min(left, right) <= threshold) edges |= left <= right ? DockEdges.Left : DockEdges.Right;
        if (Math.Min(top, bottom) <= threshold) edges |= top <= bottom ? DockEdges.Top : DockEdges.Bottom;
        if (!resizing) return new(Align(bounds, work, edges), edges);

        // 缩放只修正吸附的边，不平移整张便签，保留用户拖动的另一侧。
        var x = edges.HasFlag(DockEdges.Left) ? work.Left : bounds.Left;
        var y = edges.HasFlag(DockEdges.Top) ? work.Top : bounds.Top;
        var r = edges.HasFlag(DockEdges.Right) ? work.Right : bounds.Right;
        var b = edges.HasFlag(DockEdges.Bottom) ? work.Bottom : bounds.Bottom;
        return new(new(x, y, r - x, b - y), edges);
    }

    // 越拖越远时立即放行，避免每一帧的小位移都被吸回，永远无法超过阈值。
    internal static bool ShouldSnap(double distance, double threshold, double? previousDistance)
        => distance <= threshold && (!previousDistance.HasValue || distance <= previousDistance.Value + 0.01);
}
