namespace BALAN.App;

/// <summary>
/// 二重起動の防止。設定フォルダのロックファイルを排他で開き続ける(開けたら最初のインスタンス)。
/// OS が終了時(クラッシュ含む)に必ず解放するので、古いロックが残って起動できなくなることがない。Windows / Linux 共通。
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;

    private SingleInstance(FileStream stream) => _lock = stream;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BALAN", "balan.lock");

    /// <summary>最初のインスタンスなら取得したロック、既に起動中なら null。ロックファイルを作れない場合は(起動を妨げないよう)制限なしで続行する。</summary>
    public static SingleInstance? TryAcquire(string path, out bool alreadyRunning)
    {
        alreadyRunning = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new SingleInstance(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            alreadyRunning = true; // 共有違反 = 他のプロセスが保持中
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _lock.Dispose();
}
