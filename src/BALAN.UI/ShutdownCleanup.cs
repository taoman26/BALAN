namespace BALAN.UI;

/// <summary>
/// 終了時の後始末を、同期的に・時間を区切って・例外を出さずに実行する。
/// Windows の OS シャットダウン/ログオフでは、ShutdownRequested をキャンセルする(= 後で改めて終了する)と
/// シャットダウン自体が阻止されてしまうため、後始末はキャンセルせずにその場で済ませる必要がある。
/// </summary>
public static class ShutdownCleanup
{
    /// <summary>
    /// <paramref name="cleanup"/> を別スレッドで実行して最大 <paramref name="timeout"/> 待つ(UI スレッドでの同期待ちによるデッドロックを避けるため)。
    /// 失敗・タイムアウトしても例外は出さず、終了を妨げない。
    /// </summary>
    /// <returns>時間内に正常に完了したか。</returns>
    public static bool Run(Func<Task> cleanup, TimeSpan timeout)
    {
        try
        {
            return Task.Run(cleanup).Wait(timeout);
        }
        catch
        {
            return false;
        }
    }
}
