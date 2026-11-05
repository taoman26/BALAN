namespace BALAN.Core.Abstractions;

/// <summary>画面下部のステータス行などに出す、ユーザー向けの短いメッセージ。</summary>
public interface IAppLog
{
    void Info(string message);
}

public sealed class NullAppLog : IAppLog
{
    public void Info(string message)
    {
    }
}

/// <summary>メッセージをイベントで UI へ流す(接続・ペアリングの経過、権限エラーなど)。</summary>
public sealed class MessageLog : IAppLog
{
    /// <summary>任意のスレッドから発火される。</summary>
    public event Action<string>? Logged;

    public void Info(string message) => Logged?.Invoke(message);
}
