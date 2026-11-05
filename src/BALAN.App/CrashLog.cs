using Avalonia.Threading;

namespace BALAN.App;

/// <summary>
/// 想定外の例外を %APPDATA%\BALAN\crash.log に記録する。UI スレッドの例外は記録して続行する(常駐アプリなので落とさない)。
/// </summary>
internal static class CrashLog
{
    private static readonly object Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BALAN", "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Avalonia の初期化後に呼ぶ。</summary>
    public static void InstallUiHandler() =>
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Write("UI", e.Exception);
            e.Handled = true;
        };

    public static void Write(string source, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
            }
        }
        catch
        {
            // ログ書き込みの失敗で更に落とさない。
        }
    }
}
