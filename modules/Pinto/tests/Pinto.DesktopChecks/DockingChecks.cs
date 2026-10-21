using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Pinto.Core;
using Pinto.Services;
using Pinto.Views;

namespace Pinto.DesktopChecks;

internal static class DockingChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);

    public static async Task RunAsync(string root, Action<string, bool> check)
    {
        var workArea = SystemParameters.WorkArea;
        var data = new AppData
        {
            Notes = [new() { Left = workArea.Left + 60, Top = workArea.Top + 60 },
                new() { IsVisible = false, Left = workArea.Left + 100, Top = workArea.Top + 100, DockEdges = DockEdges.Left | DockEdges.Top }]
        };
        var store = new DataStore(Path.Combine(root, "docking-data"));
        store.Save(data);
        AppCoordinator? coordinator = null;
        try
        {
            coordinator = new(store, store.Load());
            await Task.Delay(180);
            var window = coordinator.Windows[data.Notes[0].Id];
            var handle = new WindowInteropHelper(window).Handle;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) throw new InvalidOperationException("无法读取测试显示器工作区。");
            var work = monitor.Work;
            work.Left += 2; work.Top += 2; work.Right -= 2; work.Bottom -= 2;
            foreach (var (label, edges) in new[]
            {
                ("左边", DockEdges.Left), ("右边", DockEdges.Right), ("上边", DockEdges.Top), ("下边", DockEdges.Bottom),
                ("左上角", DockEdges.Left | DockEdges.Top), ("右上角", DockEdges.Right | DockEdges.Top),
                ("左下角", DockEdges.Left | DockEdges.Bottom), ("右下角", DockEdges.Right | DockEdges.Bottom)
            })
            {
                var menu = DockingMenu(window);
                var item = menu.Items.OfType<MenuItem>().Single(entry => Equals(entry.Header, label));
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Task.Delay(60);
                GetWindowRect(handle, out var before);
                var rect = before;
                // 自动吸附检查从远离目标的位置开始；已贴边向内的小位移应当允许脱离。
                window.SetDockEdges(DockEdges.None);
                SetWindowPos(handle, 0, work.Left + (work.Width - before.Width) / 2, work.Top + (work.Height - before.Height) / 2, before.Width, before.Height, 0x0004 | 0x0010);
                rect.Left = edges.HasFlag(DockEdges.Left) ? work.Left + 6 : edges.HasFlag(DockEdges.Right) ? work.Right - before.Width - 6 : work.Left + (work.Width - before.Width) / 2;
                rect.Top = edges.HasFlag(DockEdges.Top) ? work.Top + 7 : edges.HasFlag(DockEdges.Bottom) ? work.Bottom - before.Height - 7 : work.Top + (work.Height - before.Height) / 2;
                rect.Right = rect.Left + before.Width;
                rect.Bottom = rect.Top + before.Height;
                var moved = Move(handle, rect, resizing: false);
                await Task.Delay(80);
                GetWindowRect(handle, out var actual);
                check($"{label}：菜单贴边与原生拖动吸附、记忆一致", OnEdges(before, work, edges) && OnEdges(moved, work, edges) && OnEdges(actual, work, edges) && window.DockedEdges == edges && store.Load().Data.Notes[0].DockEdges == edges);
            }
            window.Width += 30;
            window.Height += 20;
            await Task.Delay(180);
            GetWindowRect(handle, out var resized);
            check("已贴右下角时调整大小仍保持相邻两边对齐", OnEdges(resized, work, DockEdges.Right | DockEdges.Bottom));
            var proposal = new Rect { Left = resized.Left - 20, Top = resized.Top - 10, Right = work.Right - 5, Bottom = work.Bottom - 7 };
            var sizing = Move(handle, proposal, resizing: true);
            await Task.Delay(80);
            check("原生缩放吸附保持未吸附边的位置", sizing.Left == proposal.Left && sizing.Top == proposal.Top && sizing.Right == work.Right && sizing.Bottom == work.Bottom);

            GetWindowRect(handle, out var current);
            var free = new Rect { Left = work.Left + (work.Width - current.Width) / 2, Top = work.Top + (work.Height - current.Height) / 2 };
            free.Right = free.Left + current.Width;
            free.Bottom = free.Top + current.Height;
            Move(handle, free, resizing: false);
            await Task.Delay(100);
            check("拖离工作区边缘解除贴边并保存", window.DockedEdges == DockEdges.None && store.Load().Data.Notes[0].DockEdges == DockEdges.None);

            window.SetDockEdges(DockEdges.Right | DockEdges.Bottom);
            GetWindowRect(handle, out var dragStart);
            Rect beforeRelease = default;
            SendMessage(handle, 0x0231, 0, 0);
            try
            {
                for (var step = 0; step < 12; step++)
                {
                    GetWindowRect(handle, out var next);
                    next.Left -= 3; next.Right -= 3;
                    next.Top -= 3; next.Bottom -= 3;
                    ApplyMovingRectangle(handle, next);
                }
                GetWindowRect(handle, out beforeRelease);
            }
            finally { SendMessage(handle, 0x0232, 0, 0); }
            await Task.Delay(100);
            GetWindowRect(handle, out var dragged);
            check($"贴边后连续小幅拖动能脱离右下角（起点={dragStart.Left},{dragStart.Top}；松手前={beforeRelease.Left},{beforeRelease.Top}；松手后={dragged.Left},{dragged.Top}）", dragged.Left <= dragStart.Left - 30 && dragged.Top <= dragStart.Top - 30 && window.DockedEdges == DockEdges.None);
            window.SetDockEdges(DockEdges.Right | DockEdges.Bottom);
            coordinator.HideAll();
            coordinator.Dispose();
            coordinator = new(store, store.Load());
            await Task.Delay(120);
            check("重启保持各便签的贴边记忆及隐藏状态", coordinator.Windows.Values.All(note => !note.IsVisible) && coordinator.Windows[data.Notes[0].Id].DockedEdges == (DockEdges.Right | DockEdges.Bottom) && coordinator.Windows[data.Notes[1].Id].DockedEdges == (DockEdges.Left | DockEdges.Top));
            foreach (var note in coordinator.Notes)
            {
                coordinator.ShowNote(note.Id);
                await Task.Delay(100);
                var restored = coordinator.Windows[note.Id];
                GetWindowRect(new WindowInteropHelper(restored).Handle, out var rect);
                check($"隐藏便签恢复后重新对齐记忆方向 {note.DockEdges}", OnEdges(rect, work, note.DockEdges));
            }
            window = coordinator.Windows[data.Notes[0].Id];
            var cancel = DockingMenu(window).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "取消贴边"));
            cancel.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check("右键取消贴边清除已保存的记忆", window.DockedEdges == DockEdges.None && store.Load().Data.Notes[0].DockEdges == DockEdges.None);

            var peer = coordinator.Windows[data.Notes[1].Id];
            peer.SetDockEdges(DockEdges.None);
            window.Width = peer.Width = window.MinWidth;
            window.Height = peer.Height = window.MinHeight;
            await Task.Delay(100);
            handle = new WindowInteropHelper(window).Handle;
            var peerHandle = new WindowInteropHelper(peer).Handle;
            Rect lastProposal = default;
            foreach (var direction in new[] { "左", "右", "上", "下" })
            {
                GetWindowRect(handle, out var size);
                GetWindowRect(peerHandle, out var peerSize);
                var peerLeft = work.Left + 40 + (direction == "左" ? size.Width : 0);
                var peerTop = work.Top + 40 + (direction == "上" ? size.Height : 0);
                SetWindowPos(peerHandle, 0, peerLeft, peerTop, peerSize.Width, peerSize.Height, 0x0004 | 0x0010);
                window.SetDockEdges(DockEdges.None);
                SetWindowPos(handle, 0, work.Left + (work.Width - size.Width) / 2, work.Top + (work.Height - size.Height) / 2, size.Width, size.Height, 0x0004 | 0x0010);
                var left = direction == "左" ? peerLeft - size.Width - 2 : direction == "右" ? peerLeft + peerSize.Width + 2 : peerLeft;
                var top = direction == "上" ? peerTop - size.Height - 2 : direction == "下" ? peerTop + peerSize.Height + 2 : peerTop;
                lastProposal = new() { Left = left + 6, Top = top + 7, Right = left + 6 + size.Width, Bottom = top + 7 + size.Height };
                var snapped = Move(handle, lastProposal, resizing: false);
                await Task.Delay(80);
                GetWindowRect(handle, out var actual);
                check($"便签间{direction}方贴边并对齐邻边", snapped.Left == left && snapped.Top == top && actual.Left == left && actual.Top == top && window.DockedEdges == DockEdges.None);
                var dx = direction == "左" ? -3 : direction == "右" ? 3 : 0;
                var dy = direction == "上" ? -3 : direction == "下" ? 3 : 0;
                SendMessage(handle, 0x0231, 0, 0);
                try
                {
                    for (var step = 0; step < 12; step++)
                    {
                        GetWindowRect(handle, out var next);
                        next.Left += dx; next.Right += dx; next.Top += dy; next.Bottom += dy;
                        ApplyMovingRectangle(handle, next);
                    }
                }
                finally { SendMessage(handle, 0x0232, 0, 0); }
                await Task.Delay(80);
                GetWindowRect(handle, out var separated);
                check($"便签间{direction}方贴边后小幅连续拖动可脱离", Math.Abs(separated.Left - actual.Left) + Math.Abs(separated.Top - actual.Top) >= 30);
            }
            coordinator.HideNote(peer.Model.Id);
            var hiddenResult = Move(handle, lastProposal, resizing: false);
            await Task.Delay(80);
            check("隐藏的便签不参与相互吸附", hiddenResult.Left == lastProposal.Left && hiddenResult.Top == lastProposal.Top);
            coordinator.ShowNote(peer.Model.Id);
            var beside = Move(handle, lastProposal, resizing: false);
            await Task.Delay(80);
            GetWindowRect(peerHandle, out var peerBounds);
            var freePeer = new Rect { Left = peerBounds.Right + 60, Top = peerBounds.Top + 40 };
            freePeer.Right = freePeer.Left + beside.Width;
            freePeer.Bottom = freePeer.Top + beside.Height;
            var released = Move(handle, freePeer, resizing: false);
            await Task.Delay(80);
            check("拖离另一张便签解除吸附", released.Left == freePeer.Left && released.Top == freePeer.Top);
            Move(handle, lastProposal, resizing: false);
            await Task.Delay(80);
            GetWindowRect(handle, out var savedPosition);
            coordinator.HideAll();
            coordinator.Dispose();
            coordinator = new(store, store.Load());
            coordinator.ShowAll();
            await Task.Delay(150);
            window = coordinator.Windows[data.Notes[0].Id];
            peer = coordinator.Windows[data.Notes[1].Id];
            handle = new WindowInteropHelper(window).Handle;
            GetWindowRect(handle, out var restarted);
            GetWindowRect(new WindowInteropHelper(peer).Handle, out var restartedPeer);
            check("重启保留便签间 2px 间距且不误判为屏幕贴边", Math.Abs(restarted.Left - savedPosition.Left) <= 1 && Math.Abs(restarted.Top - savedPosition.Top) <= 1 && Math.Abs(restarted.Top - restartedPeer.Bottom - 2) <= 1 && window.DockedEdges == DockEdges.None);
            peer.Left += 20;
            await Task.Delay(80);
            GetWindowRect(handle, out var independent);
            check("贴边后的便签仍可独立移动", independent.Left == restarted.Left && independent.Top == restarted.Top);
        }
        finally { coordinator?.Dispose(); }
    }

    private static MenuItem DockingMenu(NoteWindow window)
        => window.RebuildContextMenu().Items.OfType<MenuItem>().Single(item => Equals(item.Header, "贴边固定"));

    private static Rect Move(nint handle, Rect rect, bool resizing)
    {
        SendMessage(handle, 0x0231, 0, 0);
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            Marshal.StructureToPtr(rect, pointer, false);
            SendMessage(handle, resizing ? 0x0214 : 0x0216, resizing ? 8 : 0, pointer);
            var result = Marshal.PtrToStructure<Rect>(pointer);
            SetWindowPos(handle, 0, result.Left, result.Top, result.Width, result.Height, 0x0004 | 0x0010);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
            SendMessage(handle, 0x0232, 0, 0);
        }
    }

    private static void ApplyMovingRectangle(nint handle, Rect rect)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            Marshal.StructureToPtr(rect, pointer, false);
            SendMessage(handle, 0x0216, 0, pointer);
            var result = Marshal.PtrToStructure<Rect>(pointer);
            SetWindowPos(handle, 0, result.Left, result.Top, result.Width, result.Height, 0x0004 | 0x0010);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static bool OnEdges(Rect rect, Rect work, DockEdges edges)
        => (!edges.HasFlag(DockEdges.Left) || rect.Left == work.Left)
        && (!edges.HasFlag(DockEdges.Right) || rect.Right == work.Right)
        && (!edges.HasFlag(DockEdges.Top) || rect.Top == work.Top)
        && (!edges.HasFlag(DockEdges.Bottom) || rect.Bottom == work.Bottom);
}
