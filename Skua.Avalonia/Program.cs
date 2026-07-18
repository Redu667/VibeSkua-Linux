using Avalonia;
using Velopack;

namespace Skua.Avalonia;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't
    // initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Per-launch log routing FIRST (before anything logs or the native
        // bridge loads): each launch gets its own timestamped log set under
        // ~/.config/Skua/logs/, and the paths are published as env vars the
        // native bridge honors. Must run before BuildAvaloniaApp/native init.
        Services.SessionLog.Init(args, DateTime.Now);

        // Never let the app vanish silently: log unhandled exceptions to a file
        // (and stderr) so failures are diagnosable without a terminal. Unobserved
        // task exceptions are marked observed so a stray background failure — e.g.
        // an async command's error path — doesn't tear the process down.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };

        // Velopack must run first: it handles the install / update / uninstall
        // hooks the AppImage invokes, and exits early during those. On a normal
        // launch it is a no-op and control falls through to the app. Wrapped so
        // a missing/!packaged context never blocks starting the UI.
        try
        {
            VelopackApp.Build().Run();
        }
        catch
        {
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash(ex, "StartWithClassicDesktopLifetime");
            throw;
        }
    }

    private static readonly object _logLock = new();

    private static void LogCrash(Exception? ex, string source)
    {
        if (ex is null)
            return;

        // Filter the benign Avalonia/Tmds.DBus shutdown race: on exit the D-Bus
        // connection (tray / desktop portal) disposes and posts to an
        // already-closing dispatcher, throwing TaskCanceledException on a
        // background thread. It's harmless and was flooding the crash log with
        // identical stacks — drop it.
        if (ex is System.Threading.Tasks.TaskCanceledException && ex.ToString().Contains("Tmds.DBus"))
            return;

        string line = $"[{source}] {ex}";
        try
        {
            System.Console.Error.WriteLine(line);
        }
        catch
        {
        }
        try
        {
            // This launch's crash log under logs/ (set by SessionLog.Init);
            // falls back to the legacy single file if init didn't run.
            string path = Services.SessionLog.CrashLogPath
                ?? System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skua", "vibeskua-crash.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            lock (_logLock)
            {
                System.IO.File.AppendAllText(
                    path, $"{System.DateTime.UtcNow:o} {line}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    // Avalonia configuration, don't remove; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
