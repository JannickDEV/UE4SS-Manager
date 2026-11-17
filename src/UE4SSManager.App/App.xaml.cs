using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UE4SSManager.Core;

namespace UE4SSManager.App;

public partial class App : Application
{
    public static string CrashLogPath => Path.Combine(AppSettings.CacheDir, "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog("Task", args.Exception);
            args.SetObserved();
        };
        base.OnStartup(e);
    }

    private static bool _showingError;
    private static DateTime _lastErrorBox;

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("UI", e.Exception);
        e.Handled = true;
        // One box at a time, and not for every repeat of the same failure: a message box pumps messages,
        // which can re-run the failing layout and stack error boxes until the process overflows its stack.
        if (_showingError || DateTime.Now - _lastErrorBox < TimeSpan.FromSeconds(5)) return;
        _showingError = true;
        try
        {
            MessageBox.Show($"{e.Exception.Message}\n\nDetails were saved to {CrashLogPath}", "UE4SS Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
        finally
        {
            _showingError = false;
            _lastErrorBox = DateTime.Now;
        }
    }

    /// <summary>Appends the full exception to %LocalAppData%\UE4SS-Manager\crash.log so crashes can be diagnosed.</summary>
    public static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.CacheDir);
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}
