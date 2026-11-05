using BALAN.Device.Transport;
using BALAN.Device.Linux.Native;

namespace BALAN.Device.Linux;

/// <summary>
/// L2CAP の制御チャネル(PSM 0x11)と割り込みチャネル(PSM 0x13)で接続した Balance Board。
/// HID の転送ヘッダ(入力 0xA1 / 出力 0xA2)をここで付け外しして、HidSharp 経由の接続と同じ形のレポートを扱えるようにする。
/// </summary>
internal sealed class L2capBoardConnection : IBoardConnection
{
    private const byte InputHeader = 0xA1;
    private const byte OutputHeader = 0xA2;
    private const int PollMilliseconds = 200;

    private readonly int _controlFd;
    private readonly int _interruptFd;
    private readonly object _readGate = new();
    private readonly object _writeGate = new();
    private readonly byte[] _receive = new byte[64];
    private volatile bool _disposed;

    public L2capBoardConnection(int controlFd, int interruptFd)
    {
        _controlFd = controlFd;
        _interruptFd = interruptFd;
    }

    public int InputReportLength => 22;

    public int Read(byte[] buffer)
    {
        lock (_readGate)
        {
            ThrowIfDisposed();
            var poll = new LibC.PollFd { fd = _interruptFd, events = LibC.PollIn };
            var ready = LibC.poll(ref poll, 1, PollMilliseconds);
            if (ready < 0)
            {
                throw new IOException("poll failed");
            }

            if (ready == 0)
            {
                return 0; // タイムアウト
            }

            if ((poll.revents & LibC.PollIn) == 0)
            {
                throw new IOException("The Balance Board connection was lost.");
            }

            var received = (int)LibC.recv(_interruptFd, _receive, (nuint)_receive.Length, 0);
            if (received <= 0)
            {
                throw new IOException("The Balance Board connection was closed.");
            }

            var offset = _receive[0] == InputHeader ? 1 : 0;
            var count = Math.Min(received - offset, buffer.Length);
            Array.Copy(_receive, offset, buffer, 0, count);
            return count;
        }
    }

    public void Write(byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (_writeGate)
        {
            ThrowIfDisposed();
            var packet = new byte[report.Length + 1];
            packet[0] = OutputHeader;
            Array.Copy(report, 0, packet, 1, report.Length);
            if (LibC.send(_interruptFd, packet, (nuint)packet.Length, 0) < 0)
            {
                throw new IOException("Failed to send a report to the Balance Board.");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 読み取り中の poll を終わらせてから(最大 200 ms)、ソケットを閉じる。
        LibC.shutdown(_interruptFd, LibC.ShutRdWr);
        lock (_readGate)
        {
            lock (_writeGate)
            {
                LibC.close(_interruptFd);
                LibC.close(_controlFd);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(L2capBoardConnection));
        }
    }
}
