using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Pinto.Core;
using Pinto.Services;
using Pinto.Views;

namespace Pinto.DesktopChecks;

internal static class Program
{
    private static readonly List<string> Results = [];
    private static int _failed;
    [STAThread]
    public static int Main(string[] args)
    {
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);
        var root = Path.Combine(Path.GetFullPath(args[0]), "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await LayoutChecks.RunAsync(root, Check);
                await DockingChecks.RunAsync(root, Check);
                await VisualDesignChecks.RunAsync(root, Check);
                using var coordinator = new AppCoordinator(new DataStore(Path.Combine(root, "edit-data")), new(new AppData { Notes = [new NoteData()] }, false));
                await Task.Delay(100);
                await EmptyEditChecks.RunAsync(coordinator.Windows.Values.Single(), coordinator, Check);
            }
            catch (Exception error) { _failed++; Results.Add("异常：" + error); }
            finally
            {
                File.WriteAllLines(Path.Combine(root, "desktop-checks.txt"), Results);
                foreach (var result in Results) Console.WriteLine(result);
                app.Shutdown();
            }
        });
        app.Run();
        return _failed == 0 ? 0 : 1;
    }
    private static void Check(string message, bool condition)
    {
        Results.Add($"{(condition ? "通过" : "失败")}：{message}");
        if (!condition) _failed++;
    }
}


