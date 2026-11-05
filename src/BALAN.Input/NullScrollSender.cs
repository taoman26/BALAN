using BALAN.Core.Abstractions;

namespace BALAN.Input;

/// <summary>スクロールを実際には送らない(未対応 OS での開発・画面確認用)。送った量だけ数える。</summary>
public sealed class NullScrollSender : IScrollSender
{
    private long _total;

    /// <summary>これまでに送った(はずの)ホイール量の合計。</summary>
    public long TotalDelta => Interlocked.Read(ref _total);

    public void SendVertical(int wheelDelta) => Interlocked.Add(ref _total, wheelDelta);
}
