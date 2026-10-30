using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CheatStringFinder.App.ViewModels;

namespace CheatStringFinder.App;

/// <summary>
/// Cheat String Finder startup. A file given on the command line (or dropped on the exe) is chosen right away.
/// Developer aid: --snapshot out-dir [clean-pid,injected-pid] renders the pages to PNG, running a comparison when given.
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Draw in software: some graphics drivers leave stale pixels behind when part of the window
        // changes, e.g. two scrollbar thumbs and one menu entry drawn over another in the sidebar during a scan.
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show($"Something went wrong: {ex.Exception.Message}", "Cheat String Finder", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        var window = new MainWindow();
        MainWindow = window;
        var vm = (FinderViewModel)window.DataContext;
        if (e.Args.Length >= 2 && e.Args[0] == "--snapshot")
        {
            await Snapshot(window, vm, e.Args[1], e.Args.Length > 2 ? e.Args[2] : null);
            Shutdown();
            return;
        }
        window.Show();
        if (e.Args.Length > 0 && File.Exists(e.Args[0])) vm.CheatFile = e.Args[0];
    }

    static async Task Snapshot(MainWindow window, FinderViewModel vm, string dir, string? file)
    {
        Directory.CreateDirectory(dir);
        window.Width = 1440; window.Height = 900;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000; window.ShowInTaskbar = false;
        window.Show();
        await Render(window, Path.Combine(dir, "1-start.png"));
        // "clean-pid,injected-pid": compare any two processes (the developer test uses two copies of a small program).
        if (file != null && file.Split(',') is [var a, var b] && int.TryParse(a, out int cleanPid) && int.TryParse(b, out int injectedPid))
        {
            vm.CleanGame = new RatChecker.Core.GameProcess(cleanPid, "clean", "", null);
            vm.InjectedGame = new RatChecker.Core.GameProcess(injectedPid, "injected", "", null);
            await vm.FindAsync();
            await Render(window, Path.Combine(dir, "2-results.png"));
        }
        vm.Page = "Help";
        await Render(window, Path.Combine(dir, "3-help.png"));
    }

    static async Task Render(Window w, string file)
    {
        await w.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(150);
        var root = (FrameworkElement)w.Content;
        var dpi = VisualTreeHelper.GetDpi(root);
        var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
