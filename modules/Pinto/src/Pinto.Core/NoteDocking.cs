namespace Pinto.Core;

/// <summary>便签之间只吸附相接的外边缘；屏幕已吸附的坐标轴保持不变。</summary>
public static class NoteDocking
{
    private readonly record struct Candidate(double Position, WindowBounds Peer, double Distance);

    public static WindowBounds Snap(WindowBounds bounds, WindowBounds work, IEnumerable<WindowBounds> peers, double threshold, DockEdges screenEdges = DockEdges.None, WindowBounds? previousBounds = null, double gap = 0)
    {
        var lockX = (screenEdges & (DockEdges.Left | DockEdges.Right)) != 0;
        var lockY = (screenEdges & (DockEdges.Top | DockEdges.Bottom)) != 0;
        Candidate? horizontal = null, vertical = null;
        foreach (var peer in peers)
        {
            if (!lockX && Overlap(bounds.Top, bounds.Bottom, peer.Top, peer.Bottom))
            {
                Consider(ref horizontal, peer.Left - bounds.Width - gap, bounds.Left, previousBounds?.Left, work.Left, work.Right - bounds.Width, peer);
                Consider(ref horizontal, peer.Right + gap, bounds.Left, previousBounds?.Left, work.Left, work.Right - bounds.Width, peer);
            }
            if (!lockY && Overlap(bounds.Left, bounds.Right, peer.Left, peer.Right))
            {
                Consider(ref vertical, peer.Top - bounds.Height - gap, bounds.Top, previousBounds?.Top, work.Top, work.Bottom - bounds.Height, peer);
                Consider(ref vertical, peer.Bottom + gap, bounds.Top, previousBounds?.Top, work.Top, work.Bottom - bounds.Height, peer);
            }
        }

        if (horizontal is { } x && vertical is { } y)
        {
            var combined = bounds with { Left = x.Position, Top = y.Position };
            if (Fits(combined, work) && Overlap(combined.Top, combined.Bottom, x.Peer.Top, x.Peer.Bottom)
                && Overlap(combined.Left, combined.Right, y.Peer.Left, y.Peer.Right)) return combined;
            return x.Distance <= y.Distance ? AlongHorizontal(x) : AlongVertical(y);
        }
        if (horizontal is { } h) return AlongHorizontal(h);
        if (vertical is { } v) return AlongVertical(v);
        return bounds;

        void Consider(ref Candidate? best, double position, double origin, double? previousOrigin, double minimum, double maximum, WindowBounds peer)
        {
            var distance = Math.Abs(position - origin);
            var previousDistance = previousOrigin.HasValue ? Math.Abs(position - previousOrigin.Value) : (double?)null;
            if (position < minimum || position > maximum || !EdgeDocking.ShouldSnap(distance, threshold, previousDistance) || (best.HasValue && distance >= best.Value.Distance)) return;
            best = new(position, peer, distance);
        }
        WindowBounds AlongHorizontal(Candidate candidate)
        {
            var result = bounds with { Left = candidate.Position };
            if (!lockY) result = result with { Top = AlignNearest(bounds.Top, previousBounds?.Top, candidate.Peer.Top, candidate.Peer.Bottom - bounds.Height, work.Top, work.Bottom - bounds.Height) };
            return Fits(result, work) ? result : bounds;
        }
        WindowBounds AlongVertical(Candidate candidate)
        {
            var result = bounds with { Top = candidate.Position };
            if (!lockX) result = result with { Left = AlignNearest(bounds.Left, previousBounds?.Left, candidate.Peer.Left, candidate.Peer.Right - bounds.Width, work.Left, work.Right - bounds.Width) };
            return Fits(result, work) ? result : bounds;
        }
        double AlignNearest(double origin, double? previousOrigin, double first, double second, double minimum, double maximum)
        {
            var target = Math.Abs(first - origin) <= Math.Abs(second - origin) ? first : second;
            var previousDistance = previousOrigin.HasValue ? Math.Abs(target - previousOrigin.Value) : (double?)null;
            return target >= minimum && target <= maximum && EdgeDocking.ShouldSnap(Math.Abs(target - origin), threshold, previousDistance) ? target : origin;
        }
    }

    private static bool Overlap(double start, double end, double peerStart, double peerEnd) => Math.Min(end, peerEnd) > Math.Max(start, peerStart);
    private static bool Fits(WindowBounds bounds, WindowBounds work) => bounds.Left >= work.Left && bounds.Top >= work.Top && bounds.Right <= work.Right && bounds.Bottom <= work.Bottom;
}
