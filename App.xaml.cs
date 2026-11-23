using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MiniSoundpad;

public partial class App : Application
{
    public static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniSoundpad", "log.txt");

    private const string MutexName = "MiniSoundpad_SingleInstance_9f3c";
    private const string ShowEventName = "MiniSoundpad_ShowWindow_9f3c";
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // ---- 只允许运行一个：已经在托盘里了就把它叫出来，自己退出 ----
        _mutex = new Mutex(true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Environment.Exit(0); // 直接退出，不再创建主窗口
            return;
        }
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showEvent = showEvent;
        var listener = new Thread(() =>
        {
            while (showEvent.WaitOne())
                Dispatcher.InvokeAsync(() => (MainWindow as Views.MainWindow)?.ShowFromTray());
        }) { IsBackground = true };
        listener.Start();

        Log("==== 启动 ====");
        DispatcherUnhandledException += OnUnhandled;
        // 后台线程（音频线程等）的未处理异常会直接杀掉进程，这里至少把原因记下来
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("后台线程崩溃: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("Task 异常: " + args.Exception);
            args.SetObserved();
        };
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_showEvent != null) Log($"退出，代码 {e.ApplicationExitCode}");
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("UI 线程异常: " + e.Exception);
        MessageBox.Show(e.Exception.ToString(), "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
