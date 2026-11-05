using BALAN.Device.Transport;

namespace BALAN.Device.Linux;

/// <summary>
/// 自動ペアリング(<see cref="LinuxBoardPairing"/>)が確立した接続を、
/// 接続待ち中の BalanceBoardService へ渡す受け渡し場所(<see cref="LinuxBoardTransport"/> が受け取る)。
/// </summary>
public sealed class LinuxBoardLink
{
    private readonly object _gate = new();
    private IBoardConnection? _pending;

    internal bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null;
            }
        }
    }

    internal void Set(IBoardConnection connection)
    {
        IBoardConnection? old;
        lock (_gate)
        {
            old = _pending;
            _pending = connection;
        }

        old?.Dispose();
    }

    internal IBoardConnection? Take()
    {
        lock (_gate)
        {
            var connection = _pending;
            _pending = null;
            return connection;
        }
    }

    internal void Clear() => Take()?.Dispose();
}
