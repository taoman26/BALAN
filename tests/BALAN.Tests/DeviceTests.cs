using System.Collections.Concurrent;
using BALAN.Core.Domain;
using BALAN.Device;
using BALAN.Device.Protocol;
using BALAN.Device.Transport;
using BALAN.Device.Windows;
using Xunit;

namespace BALAN.Tests;

public class ProtocolTests
{
    /// <summary>TR, BR, TL, BL の順(実機の並び)で 0 / 17 / 34 kg の生値を並べた較正データ。</summary>
    internal static byte[] CalibrationBytes(ushort zero = 1000, ushort mid = 2000, ushort high = 3000)
    {
        var bytes = new List<byte>();
        foreach (var v in new[] { zero, mid, high })
        {
            for (var s = 0; s < 4; s++)
            {
                bytes.Add((byte)(v >> 8));
                bytes.Add((byte)v);
            }
        }

        return bytes.ToArray();
    }

    [Fact]
    public void WriteRegisterLayout()
    {
        var r = BoardProtocol.WriteRegister(0xA400F0, 0x55);
        Assert.Equal(22, r.Length);
        Assert.Equal(new byte[] { 0x16, 0x04, 0xA4, 0x00, 0xF0, 0x01, 0x55 }, r.Take(7).ToArray());
    }

    [Fact]
    public void ReadRegisterLayout()
    {
        Assert.Equal(new byte[] { 0x17, 0x04, 0xA4, 0x00, 0x24, 0x00, 0x18 }, BoardProtocol.ReadRegister(0xA40024, 24));
    }

    [Fact]
    public void ReportModeIsContinuousExtension8()
    {
        Assert.Equal(new byte[] { 0x12, 0x04, 0x32 }, BoardProtocol.SetReportModeExtension8());
    }

    [Fact]
    public void ParsesSensorOrderTrBrTlBl()
    {
        // ID, ボタン 2, TR=0x0102, BR=0x0304, TL=0x0506, BL=0x0708
        var report = new byte[] { 0x32, 0, 0, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        Assert.True(BoardReportParser.TryParseRawSensors(report, out var raw));
        Assert.Equal(new RawSensors(TopLeft: 0x0506, TopRight: 0x0102, BottomLeft: 0x0708, BottomRight: 0x0304), raw);
    }

    [Fact]
    public void ShortOrWrongReportsAreRejected()
    {
        Assert.False(BoardReportParser.TryParseRawSensors(new byte[] { 0x32, 0, 0, 1, 2 }, out _));
        Assert.False(BoardReportParser.TryParseRawSensors(new byte[11], out _));
        Assert.False(BoardReportParser.TryParseStatus(new byte[] { 0x20, 0, 0 }, out _, out _));
    }

    [Fact]
    public void ParsesBatteryRawAndLowFlag()
    {
        Assert.True(BoardReportParser.TryParseStatus(new byte[] { 0x20, 0, 0, 0x03, 0, 0, 196 }, out var ext, out var pct, out var raw, out var low));
        Assert.True(ext);
        Assert.True(low); // フラグのビット 0 = 電池残量わずか(% が 98 でも)
        Assert.Equal(196, raw);
        Assert.Equal(98, pct);
        Assert.True(BoardReportParser.TryParseStatus(new byte[] { 0x20, 0, 0, 0x02, 0, 0, 196 }, out _, out _, out _, out low));
        Assert.False(low);
    }

    [Fact]
    public void ParsesStatus()
    {
        Assert.True(BoardReportParser.TryParseStatus(new byte[] { 0x20, 0, 0, 0x02, 0, 0, 100 }, out var ext, out var battery));
        Assert.True(ext);
        Assert.Equal(50, battery);
        Assert.True(BoardReportParser.TryParseStatus(new byte[] { 0x20, 0, 0, 0x00, 0, 0, 255 }, out ext, out battery));
        Assert.False(ext);
        Assert.Equal(100, battery); // 丸め
    }

    [Fact]
    public void ParsesReadData()
    {
        var report = new byte[] { 0x21, 0, 0, 0x50, 0x00, 0xFA, 1, 2, 3, 4, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.True(BoardReportParser.TryParseReadData(report, out var address, out var error, out var data));
        Assert.Equal(0x00FA, address);
        Assert.Equal(0, error);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, data.ToArray());
    }

    [Fact]
    public void CalibrationConvertsToKilograms()
    {
        var cal = BoardCalibration.TryParse(CalibrationBytes())!;
        Assert.NotNull(cal);
        var kg = cal.ToKilograms(new RawSensors(1000, 1500, 2000, 3000));
        Assert.Equal(0, kg.TopLeft, 6);
        Assert.Equal(8.5, kg.TopRight, 6);
        Assert.Equal(17, kg.BottomLeft, 6);
        Assert.Equal(34, kg.BottomRight, 6);
        Assert.Equal(25.5, cal.ToKilograms(new RawSensors(2500, 0, 0, 0)).TopLeft, 6);
    }

    [Fact]
    public void BelowZeroIsClampedToZero()
    {
        var cal = BoardCalibration.TryParse(CalibrationBytes())!;
        Assert.Equal(0, cal.ToKilograms(new RawSensors(900, 900, 900, 900)).Total);
    }

    [Fact]
    public void InvalidCalibrationIsRejected()
    {
        Assert.Null(BoardCalibration.TryParse(new byte[24]));                // 全部 0(読み出し失敗)
        Assert.Null(BoardCalibration.TryParse(new byte[10]));                // 短い
        Assert.Null(BoardCalibration.TryParse(CalibrationBytes(2000, 2000, 3000))); // 単調でない
    }

    [Fact]
    public void WindowsPairingPinAndNameMatching()
    {
        Assert.Equal(new byte[] { 0x56, 0x34, 0x12, 0xCC, 0xBB, 0xAA }, WindowsBoardPairing.PinFromAddress(0xAABBCC123456));
    }
}

/// <summary>Balance Board(または Wii Remote)のふりをする接続。要求に応じて 0x21/0x32 を返す。</summary>
internal sealed class FakeConnection : IBoardConnection
{
    private readonly BlockingCollection<byte[]> _incoming = new();
    private readonly bool _isBoard;
    private readonly Func<int, byte[]>? _frameSource;
    private volatile bool _disposed;
    private int _frameIndex;
    private int _streamFrames;

    public FakeConnection(bool isBoard, Func<int, byte[]>? frameSource = null)
    {
        _isBoard = isBoard;
        _frameSource = frameSource;
    }

    public List<byte[]> Written { get; } = new();

    public bool IsDisposed => _disposed;

    public int InputReportLength => 22;

    /// <summary>接続後、指定数のフレームを送ったら切断(IOException)する。</summary>
    public int DisconnectAfterFrames { get; set; } = int.MaxValue;

    /// <summary>指定数のフレームを送ったら、接続は生きたまま荷重を送らなくなる(電池が弱ったときの挙動)。</summary>
    public int StallAfterFrames { get; set; } = int.MaxValue;

    public int Read(byte[] buffer)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FakeConnection));

        if (_incoming.TryTake(out var report, 20))
        {
            Array.Copy(report, buffer, Math.Min(report.Length, buffer.Length));
            return report.Length;
        }

        if (Volatile.Read(ref _streamFrames) > 0)
        {
            if (_frameIndex >= DisconnectAfterFrames) throw new IOException("device lost");
            if (_frameIndex >= StallAfterFrames)
            {
                Thread.Sleep(5);
                return 0;
            }

            var frame = _frameSource!(_frameIndex++);
            Array.Copy(frame, buffer, frame.Length);
            Thread.Sleep(1);
            return frame.Length;
        }

        return 0;
    }

    public void Write(byte[] report)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FakeConnection));
        lock (Written) Written.Add(report);

        switch (report[0])
        {
            case 0x17: // メモリ読み出し
                var address = (report[2] << 16) | (report[3] << 8) | report[4];
                var length = (report[5] << 8) | report[6];
                var data = address switch
                {
                    BoardProtocol.ExtensionIdAddress => _isBoard ? BoardProtocol.BalanceBoardExtensionId : new byte[6],
                    BoardProtocol.CalibrationAddress => ProtocolTests.CalibrationBytes(),
                    _ => new byte[length]
                };
                for (var offset = 0; offset < length; offset += 16)
                {
                    var chunk = Math.Min(16, length - offset);
                    var r = new byte[22];
                    r[0] = 0x21;
                    r[3] = (byte)((chunk - 1) << 4);
                    r[4] = (byte)((address + offset) >> 8);
                    r[5] = (byte)(address + offset);
                    Array.Copy(data, offset, r, 6, chunk);
                    _incoming.Add(r);
                }
                break;
            case 0x12: // レポートモード → 連続送信開始
                Interlocked.Exchange(ref _streamFrames, 1);
                break;
        }
    }

    public void Dispose() => _disposed = true;
}

internal sealed class FakeTransport : IBoardTransport
{
    private readonly Func<IEnumerable<FakeConnection>> _candidates;

    public FakeTransport(Func<IEnumerable<FakeConnection>> candidates) => _candidates = candidates;

    public IEnumerable<IBoardConnection> OpenCandidates() => _candidates();
}

public class BalanceBoardServiceTests
{
    private static readonly BalanceBoardOptions Fast = new(
        TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(60),
        TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500));

    private static byte[] Report(ushort tr, ushort br, ushort tl, ushort bl) => new byte[]
    {
        0x32, 0, 0,
        (byte)(tr >> 8), (byte)tr, (byte)(br >> 8), (byte)br, (byte)(tl >> 8), (byte)tl, (byte)(bl >> 8), (byte)bl, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
    };

    private static async Task<T> Within<T>(Task<T> task, int ms = 3000)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms));
        Assert.True(done == task, "timed out");
        return await task;
    }

    [Fact]
    public async Task ConnectsToBoardAndDeliversKilogramFrames()
    {
        var board = new FakeConnection(true, _ => Report(tr: 2000, br: 1000, tl: 3000, bl: 1500));
        await using var service = new BalanceBoardService(new FakeTransport(() => new[] { board }), Fast);
        var states = new ConcurrentQueue<ConnectionState>();
        service.ConnectionStateChanged += (_, s) => states.Enqueue(s);
        var first = new TaskCompletionSource<SensorFrame>();
        service.FrameReceived += (_, f) => first.TrySetResult(f);

        await service.ConnectAsync();
        var frame = await Within(first.Task);

        Assert.Equal(ConnectionState.Connected, service.State);
        Assert.Equal(34, frame.TopLeft, 6);
        Assert.Equal(17, frame.TopRight, 6);
        Assert.Equal(0, frame.BottomRight, 6);
        Assert.Equal(8.5, frame.BottomLeft, 6);
        Assert.Equal(new[] { ConnectionState.Connecting, ConnectionState.Connected }, states.ToArray());

        // 初期化手順: 拡張初期化 → ID 読み出し → 較正読み出し → LED → レポートモード
        var ids = board.Written.Select(w => w[0]).ToList();
        Assert.Equal(new byte[] { 0x16, 0x16, 0x17, 0x17, 0x11, 0x12 }, ids.Take(6).ToArray());
    }

    [Fact]
    public async Task UnexpectedDisconnectIsLoggedWithReason()
    {
        var log = new BALAN.Core.Abstractions.MessageLog();
        var messages = new ConcurrentQueue<string>();
        log.Logged += messages.Enqueue;
        var board = new FakeConnection(true, _ => Report(tr: 2000, br: 1000, tl: 3000, bl: 1500)) { DisconnectAfterFrames = 20 };
        await using var service = new BalanceBoardService(new FakeTransport(() => new[] { board }), Fast, null, log);
        var lost = new TaskCompletionSource<bool>();
        service.ConnectionStateChanged += (_, s) => { if (s == ConnectionState.Disconnected) lost.TrySetResult(true); };

        await service.ConnectAsync();
        await Within(lost.Task);

        var line = Assert.Single(messages);
        Assert.Contains("Connection lost", line);
        Assert.Contains("IOException: device lost", line);
        Assert.Contains("20 frames", line);
    }

    [Fact]
    public async Task StalledSensorsWhileConnectedAreLogged()
    {
        var log = new BALAN.Core.Abstractions.MessageLog();
        var messages = new ConcurrentQueue<string>();
        log.Logged += messages.Enqueue;
        var options = Fast with { SensorStallAfter = TimeSpan.FromMilliseconds(150) };
        var board = new FakeConnection(true, _ => Report(tr: 2000, br: 1000, tl: 3000, bl: 1500)) { StallAfterFrames = 10 };
        await using var service = new BalanceBoardService(new FakeTransport(() => new[] { board }), options, null, log);
        await service.ConnectAsync();

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (messages.IsEmpty && DateTime.UtcNow < deadline) await Task.Delay(20);

        var line = Assert.Single(messages);
        Assert.Contains("No sensor data", line);
        Assert.Contains("batteries", line);
        Assert.Equal(ConnectionState.Connected, service.State); // 切断ではない
        await Task.Delay(300);
        Assert.Single(messages); // 繰り返し出さない
    }

    [Fact]
    public async Task UserDisconnectIsNotLoggedAsLost()
    {
        var log = new BALAN.Core.Abstractions.MessageLog();
        var messages = new ConcurrentQueue<string>();
        log.Logged += messages.Enqueue;
        var board = new FakeConnection(true, _ => Report(tr: 2000, br: 1000, tl: 3000, bl: 1500));
        await using var service = new BalanceBoardService(new FakeTransport(() => new[] { board }), Fast, null, log);
        await service.ConnectAsync();
        await service.DisconnectAsync();
        Assert.Empty(messages);
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        var service = new BalanceBoardService(new FakeTransport(() => Array.Empty<FakeConnection>()), Fast);
        await service.DisposeAsync();
        await service.DisposeAsync(); // 2 回目でも例外にならない
    }

    [Fact]
    public async Task DisposingContainerWithSameInstanceRegisteredTwiceDoesNotThrow()
    {
        // Program.cs と同じ登録: 同じインスタンスを本体と IBalanceBoard の 2 つのサービス型で登録する。
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(
            services, _ => new BalanceBoardService(new FakeTransport(() => Array.Empty<FakeConnection>()), Fast));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<BALAN.Core.Abstractions.IBalanceBoard>(
            services, p => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<BalanceBoardService>(p));
        var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        _ = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<BALAN.Core.Abstractions.IBalanceBoard>(provider);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task SkipsWiiRemoteAndUsesTheBoard()
    {
        var remote = new FakeConnection(false);
        var board = new FakeConnection(true, _ => Report(1500, 1500, 1500, 1500));
        await using var service = new BalanceBoardService(new FakeTransport(() => new[] { remote, board }), Fast);
        var got = new TaskCompletionSource<SensorFrame>();
        service.FrameReceived += (_, f) => got.TrySetResult(f);

        await service.ConnectAsync();
        await Within(got.Task);

        Assert.True(remote.IsDisposed);
        Assert.False(board.IsDisposed);
    }

    [Fact]
    public async Task TimesOutWhenOnlyAWiiRemoteIsPresent()
    {
        var options = Fast with { ConnectTimeout = TimeSpan.FromMilliseconds(300) };
        await using var service = new BalanceBoardService(
            new FakeTransport(() => new[] { new FakeConnection(false) }), options);

        await Assert.ThrowsAsync<TimeoutException>(() => service.ConnectAsync());
        Assert.Equal(ConnectionState.Disconnected, service.State);
    }

    [Fact]
    public async Task DeviceLossReportsDisconnected()
    {
        var board = new FakeConnection(true, _ => Report(1500, 1500, 1500, 1500)) { DisconnectAfterFrames = 5 };
        await using var service = new BalanceBoardService(new FakeTransport(() => new[] { board }), Fast);
        var lost = new TaskCompletionSource<bool>();
        service.ConnectionStateChanged += (_, s) =>
        {
            if (s == ConnectionState.Disconnected) lost.TrySetResult(true);
        };

        await service.ConnectAsync();
        await Within(lost.Task);

        Assert.Equal(ConnectionState.Disconnected, service.State);
        Assert.True(board.IsDisposed);
    }

    [Fact]
    public async Task DisconnectStopsFramesAndClosesDevice()
    {
        var board = new FakeConnection(true, _ => Report(1500, 1500, 1500, 1500));
        await using var service = new BalanceBoardService(new FakeTransport(() => new[] { board }), Fast);
        var got = new TaskCompletionSource<SensorFrame>();
        service.FrameReceived += (_, f) => got.TrySetResult(f);
        await service.ConnectAsync();
        await Within(got.Task);

        await service.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, service.State);
        Assert.True(board.IsDisposed);
    }

    [Fact]
    public async Task CanReconnectAfterDisconnect()
    {
        var calls = 0;
        await using var service = new BalanceBoardService(new FakeTransport(() =>
        {
            calls++;
            return new[] { new FakeConnection(true, _ => Report(1500, 1500, 1500, 1500)) };
        }), Fast);

        await service.ConnectAsync();
        await service.DisconnectAsync();
        await service.ConnectAsync();

        Assert.Equal(ConnectionState.Connected, service.State);
        Assert.Equal(2, calls);
    }
}

public class SimulatedBoardTests
{
    [Fact]
    public void ScenarioCoversRestAccelUpDownAndBrake()
    {
        Assert.True(SimulatedBalanceBoard.FrameAt(1).BottomRight < 1);
        Assert.InRange(SimulatedBalanceBoard.FrameAt(8.9).BottomRight, 4, 5.1);
        Assert.InRange(SimulatedBalanceBoard.FrameAt(17.9).TopRight, 4, 5.1);
        Assert.True(SimulatedBalanceBoard.FrameAt(22).BottomLeft >= 4);
    }
}
