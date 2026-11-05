using BALAN.Core.Abstractions;
using BALAN.Core.Domain;

namespace BALAN.Core.Engine;

/// <summary>
/// デバイスとエンジンをつなぐ。荷重はエンジンへ、切断・接続失敗時はエンジンを停止状態に戻す
/// (最後に受け取った荷重が残って、スクロールが止まらなくなるのを防ぐ)。
/// </summary>
public sealed class BoardEngineBridge : IDisposable
{
    private readonly IBalanceBoard _board;
    private readonly BalanEngine _engine;

    public BoardEngineBridge(IBalanceBoard board, BalanEngine engine)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _board.FrameReceived += OnFrame;
        _board.ConnectionStateChanged += OnState;
    }

    public void Dispose()
    {
        _board.FrameReceived -= OnFrame;
        _board.ConnectionStateChanged -= OnState;
    }

    private void OnFrame(object? sender, SensorFrame frame) => _engine.OnFrame(frame);

    private void OnState(object? sender, ConnectionState state)
    {
        if (state != ConnectionState.Connected)
        {
            _engine.Reset();
        }
    }
}
