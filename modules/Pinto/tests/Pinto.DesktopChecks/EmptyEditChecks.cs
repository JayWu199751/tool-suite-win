using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pinto.Services;
using Pinto.ViewModels;
using Pinto.Views;

namespace Pinto.DesktopChecks;

internal static class EmptyEditChecks
{
    public static async Task RunAsync(NoteWindow window, AppCoordinator coordinator, Action<string, bool> check)
    {
        var model = window.Model;
        var item = Add("按 Enter 删除");
        var editor = await Edit(item, "");
        SendKey(editor, Key.Enter);
        check("编辑内容清空后 Enter 删除该项", !model.Items.Contains(item));
        model.RemoveItem(item);

        item = Add("空白失焦删除");
        editor = await Edit(item, " \t ");
        editor.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, editor, (TextBox)window.FindName("NewItemEditor")) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        check("仅空白内容移开焦点后删除该项", !model.Items.Contains(item));
        model.RemoveItem(item);

        item = Add("Esc 应保留原文");
        editor = await Edit(item, "");
        SendKey(editor, Key.Escape);
        check("清空后 Esc 取消编辑并保留原文", model.Items.Contains(item) && item.Text == "Esc 应保留原文" && !item.IsEditing);
        model.RemoveItem(item);

        var first = Add("批量提交第一项");
        var second = Add("批量提交第二项");
        first.BeginEdit(); first.Draft = "";
        second.BeginEdit(); second.Draft = " ";
        model.CommitPendingEdits();
        check("批量提交空白编辑安全移除所有项", !model.Items.Contains(first) && !model.Items.Contains(second));
        model.RemoveItem(first); model.RemoveItem(second);

        item = Add("隐藏时提交删除");
        item.BeginEdit(); item.Draft = "";
        coordinator.HideNote(model.Id);
        check("隐藏便签时提交清空并删除该项", !model.Items.Contains(item));
        check("清空删除已持久化", coordinator.Store.Load().Data.Notes.Single(note => note.Id == model.Id).Items.Count == 0);
        model.RemoveItem(item);
        check("最后一项移除后显示空便签状态", model.IsEmpty);

        ChecklistItemViewModel Add(string text)
        {
            model.NewItemText = text;
            model.AddItem();
            return model.Items.Last();
        }
        async Task<TextBox> Edit(ChecklistItemViewModel target, string text)
        {
            target.BeginEdit();
            await Task.Delay(180);
            var box = Descendants<TextBox>(window).Single(candidate => ReferenceEquals(candidate.DataContext, target));
            box.Text = text;
            return box;
        }
    }
    private static void SendKey(TextBox editor, Key key)
        => editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T candidate) yield return candidate;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
