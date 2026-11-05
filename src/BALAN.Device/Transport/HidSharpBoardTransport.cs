using BALAN.Device.Protocol;
using HidSharp;

namespace BALAN.Device.Transport;

/// <summary>HidSharp による検出・オープン(Windows / Linux 共通)。</summary>
public sealed class HidSharpBoardTransport : IBoardTransport
{
    /// <summary>レポートが来ない間も、切断や終了要求を検知できるようにする読み取りタイムアウト。</summary>
    private const int ReadTimeoutMilliseconds = 500;

    public IEnumerable<IBoardConnection> OpenCandidates()
    {
        foreach (var productId in BoardProtocol.ProductIds)
        {
            foreach (var device in DeviceList.Local.GetHidDevices(BoardProtocol.NintendoVendorId, productId))
            {
                if (device.TryOpen(out var stream))
                {
                    stream.ReadTimeout = ReadTimeoutMilliseconds;
                    yield return new HidSharpConnection(device, stream);
                }
            }
        }
    }

    private sealed class HidSharpConnection : IBoardConnection
    {
        private readonly HidStream _stream;
        private readonly int _outputLength;

        public HidSharpConnection(HidDevice device, HidStream stream)
        {
            _stream = stream;
            InputReportLength = Math.Max(device.GetMaxInputReportLength(), 22);
            _outputLength = device.GetMaxOutputReportLength();
        }

        public int InputReportLength { get; }

        public int Read(byte[] buffer)
        {
            try
            {
                return _stream.Read(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                return 0;
            }
        }

        public void Write(byte[] report)
        {
            // Windows は出力レポート長ぴったりのバッファが必要。不足分は 0 で埋める。
            var buffer = report;
            if (_outputLength > report.Length)
            {
                buffer = new byte[_outputLength];
                Array.Copy(report, buffer, report.Length);
            }

            _stream.Write(buffer);
        }

        public void Dispose() => _stream.Dispose();
    }
}
