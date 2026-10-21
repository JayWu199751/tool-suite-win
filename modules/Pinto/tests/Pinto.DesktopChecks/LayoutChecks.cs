using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Pinto.Core;
using Pinto.Services;

namespace Pinto.DesktopChecks;

internal static class LayoutChecks
{
    public static async Task RunAsync(string root, Action<string, bool> check)
    {
        var work = SystemParameters.WorkArea;
        var data = new AppData
        {
            Notes = Enumerable.Range(0, 3).Select(index => new NoteData
            {
                Title = $"布局测试 {index + 1}", IsVisible = index != 1,
                Left = work.Left + 40 + index * 35, Top = work.Top + 40 + index * 35,
                Width = 300 + index * 20, Height = 230 + index * 20
            }).ToList()
        };
        var store = new DataStore(Path.Combine(root, "layout-data"));
        store.Save(data);
        AppCoordinator? coordinator = null;
        try
        {
            coordinator = new(store, store.Load());
            await Task.Delay(180);
            check("启动按每张便签的显示状态分别恢复", coordinator.Windows.Count == 2
                && coordinator.Windows[data.Notes[0].Id].IsVisible
                && !coordinator.Windows.ContainsKey(data.Notes[1].Id)
                && coordinator.Windows[data.Notes[2].Id].IsVisible);
            var moved = coordinator.Windows[data.Notes[0].Id];
            moved.Left += 15;
            moved.Top += 21;
            moved.Width = 337;
            moved.Height = 281;
            await Task.Delay(650);
            var saved = store.Load().Data;
            check("移动和调整大小后自动记录布局", Close(saved.Notes[0].Left, moved.Left) && Close(saved.Notes[0].Top, moved.Top) && Close(saved.Notes[0].Width, moved.Width) && Close(saved.Notes[0].Height, moved.Height));
            coordinator.HideNote(data.Notes[2].Id);
            saved = store.Load().Data;
            check("隐藏某张便签不改变其他便签的状态或布局", saved.Notes[0].IsVisible && !saved.Notes[1].IsVisible && !saved.Notes[2].IsVisible && Close(saved.Notes[1].Left, work.Left + 75) && Close(saved.Notes[1].Width, 320));
            coordinator.Dispose();
            coordinator = new(store, store.Load());
            await Task.Delay(180);
            check("重启恢复可见便签各自的位置和大小", Close(saved.Notes[0].Left, coordinator.Windows[saved.Notes[0].Id].Left)
                && Close(saved.Notes[0].Top, coordinator.Windows[saved.Notes[0].Id].Top)
                && Close(saved.Notes[0].Width, coordinator.Windows[saved.Notes[0].Id].Width)
                && Close(saved.Notes[0].Height, coordinator.Windows[saved.Notes[0].Id].Height));
            check("重启保持隐藏便签无窗口且可见便签打开", coordinator.Windows.Count == 1 && coordinator.Windows[saved.Notes[0].Id].IsVisible);
            coordinator.ActivateVisibleNotes();
            check("重复启动唤起可见便签并保留隐藏状态", coordinator.Windows.Count == 1 && coordinator.Windows[saved.Notes[0].Id].IsVisible
                && !store.Load().Data.Notes[1].IsVisible);
            coordinator.ShowNote(saved.Notes[1].Id);
            coordinator.Flush();
            check("托盘恢复隐藏便签保留原位置和大小并记录显示状态", coordinator.Windows[saved.Notes[1].Id].IsVisible && Close(coordinator.Windows[saved.Notes[1].Id].Width, saved.Notes[1].Width) && store.Load().Data.Notes[1].IsVisible);
            coordinator.HideAll();
            coordinator.Dispose();
            coordinator = new(store, store.Load());
            await Task.Delay(180);
            coordinator.ActivateVisibleNotes();
            check("全部隐藏时启动或重复启动均仅保留托盘", coordinator.Windows.Count == 0 && store.Load().Data.Notes.All(note => !note.IsVisible));
        }
        finally { coordinator?.Dispose(); }
    }

    private static bool Close(double? expected, double actual) => expected.HasValue && Math.Abs(expected.Value - actual) < 1;
}
