using BALAN.Core.Abstractions;

namespace BALAN.App;

/// <summary>
/// 接続・切断などの経過(<see cref="MessageLog"/>)を %APPDATA%\BALAN\balan.log に追記する。
/// 「数分で切れた」のような、エラー表示の出ない切断の原因を、後から調べられるようにするため。肥大化を防ぐため、512 KB を超えたら古いものを捨てる。
/// </summary>
internal sealed class FileLog : IDisposable
{
    private const long MaxBytes = 512 * 1024;
    private readonly MessageLog _source;
    private readonly string _path;
    private readonly object _gate = new();

    public FileLog(MessageLog source, string? path = null)
    {
        _source = source;
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BALAN", "balan.log");
        _source.Logged += OnLogged;
    }

    public void Dispose() => _source.Logged -= OnLogged;

    private void OnLogged(string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                {
                    File.Delete(_path);
                }

                File.AppendAllText(_path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // ログの失敗でアプリを止めない。
        }
    }
}
