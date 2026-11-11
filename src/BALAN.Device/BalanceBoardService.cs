using System.Diagnostics;
using BALAN.Core.Abstractions;
using BALAN.Core.Domain;
using BALAN.Device.Protocol;
using BALAN.Device.Transport;

namespace BALAN.Device;

/// <summary>接続待ちと電池確認の設定。</summary>
public sealed record BalanceBoardOptions(
    TimeSpan ConnectTimeout,
    TimeSpan ScanInterval,
    TimeSpan StatusInterval,
    TimeSpan PairingInterval,
    TimeSpan ReadResponseTimeout)
{
    public static BalanceBoardOptions Default { get; } = new(
        TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
}

/// <summary>
/// Wii Balance Board への接続と荷重の通知。
/// <see cref="ConnectAsync"/> はボードが見つかるまで一定時間スキャンし続ける。
/// 接続後は専用スレッドでレポートを読み続け、kg に直した荷重(約 100 Hz)を <see cref="FrameReceived"/> で通知する。
/// イベントは読み取りスレッドから発火される。
/// </summary>
public sealed class BalanceBoardService : IBalanceBoard, IAsyncDisposable
{
    private readonly IBoardTransport _transport;
    private readonly BalanceBoardOptions _options;
    private readonly IBoardPairing? _pairing;
    private readonly IAppLog _log;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _stateGate = new();

    private ConnectionState _state = ConnectionState.Disconnected;
    private int _batteryPercent;
    private int _batteryRaw = -1;
    private bool _batteryLow;
    private IBoardConnection? _connection;
    private Task? _readLoop;
    private volatile CancellationTokenSource? _cts;

    public BalanceBoardService(IBoardTransport transport, BalanceBoardOptions? options = null, IBoardPairing? pairing = null, IAppLog? log = null)
    {
        _log = log ?? new NullAppLog();
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? BalanceBoardOptions.Default;
        _pairing = pairing;
    }

    public ConnectionState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    public int BatteryPercent
    {
        get
        {
            lock (_stateGate)
            {
                return _batteryPercent;
            }
        }
    }

    public bool BatteryLow
    {
        get
        {
            lock (_stateGate)
            {
                return _batteryLow;
            }
        }
    }

    public event EventHandler<SensorFrame>? FrameReceived;

    public event EventHandler<ConnectionState>? ConnectionStateChanged;

    public event EventHandler<int>? BatteryChanged;

    /// <exception cref="TimeoutException">制限時間内に Balance Board が見つからなかった。</exception>
    public async Task ConnectAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != ConnectionState.Disconnected)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _cts = cts;
            SetState(ConnectionState.Connecting);

            OpenedBoard opened;
            try
            {
                opened = await ScanAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                FinishDisconnected(null);
                return;
            }
            catch
            {
                FinishDisconnected(null);
                throw;
            }

            lock (_stateGate)
            {
                _connection = opened.Connection;
            }

            SetState(ConnectionState.Connected);
            _readLoop = Task.Factory.StartNew(
                () => ReadLoop(opened.Connection, opened.Calibration, cts.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        // スキャン中(ConnectAsync がロック保持中)でも中断できるよう、ロック取得の前にキャンセルする。
        _cts?.Cancel();

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            IBoardConnection? connection;
            lock (_stateGate)
            {
                connection = _connection;
            }

            connection?.Dispose(); // 読み取りスレッドのブロックを解く

            if (_readLoop is { } loop)
            {
                await loop.ConfigureAwait(false);
                _readLoop = null;
            }

            FinishDisconnected(connection);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private int _disposed;

    /// <summary>
    /// 何度呼んでも安全。DI コンテナは、同じインスタンスを複数のサービス型(本体と <see cref="IBalanceBoard"/>)で登録すると、
    /// 終了時に Dispose を重ねて呼ぶ。2 回目で破棄済みのロックを触って例外になり、終了(シャットダウン)が止まっていた。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await DisconnectAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private sealed record OpenedBoard(IBoardConnection Connection, BoardCalibration Calibration);

    private async Task<OpenedBoard> ScanAsync(CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        var pairingDisabled = _pairing is null;
        var lastPairing = TimeSpan.MinValue;
        while (true)
        {
            token.ThrowIfCancellationRequested();

            // HidSharp の列挙・初期化はブロッキングなので、スレッドプールで実行する。
            var opened = await Task.Run(() => TryOpenBoard(token), token).ConfigureAwait(false);
            if (opened is not null)
            {
                return opened;
            }

            if (!pairingDisabled
                && (lastPairing == TimeSpan.MinValue || timer.Elapsed - lastPairing >= _options.PairingInterval))
            {
                var result = await _pairing!.TryPairAsync(token).ConfigureAwait(false);
                lastPairing = timer.Elapsed;
                if (result == PairingResult.NotSupported)
                {
                    pairingDisabled = true;
                }
                else if (result == PairingResult.Paired)
                {
                    // ペアリング直後は HID の列挙に少し時間がかかる。すぐ再スキャンに戻る。
                    await Task.Delay(_options.ScanInterval, token).ConfigureAwait(false);
                    continue;
                }
            }

            if (timer.Elapsed >= _options.ConnectTimeout)
            {
                throw new TimeoutException(
                    $"No Balance Board was found within {_options.ConnectTimeout.TotalSeconds:0} seconds. " +
                    "Press the red SYNC button (battery compartment) while connecting, or pair it in the OS Bluetooth settings.");
            }

            await Task.Delay(_options.ScanInterval, token).ConfigureAwait(false);
        }
    }

    /// <summary>候補 HID を順に開き、Balance Board であることを確かめて初期化する。違えば閉じて次へ。</summary>
    private OpenedBoard? TryOpenBoard(CancellationToken token)
    {
        foreach (var connection in _transport.OpenCandidates())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var calibration = Initialize(connection);
                if (calibration is not null)
                {
                    return new OpenedBoard(connection, calibration);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                // 開いた直後に切れた等。次の候補へ。
            }

            connection.Dispose();
        }

        return null;
    }

    /// <summary>拡張の初期化 → Balance Board の確認 → 較正値の取得 → 連続レポート開始。違うデバイスなら null。</summary>
    private BoardCalibration? Initialize(IBoardConnection connection)
    {
        connection.Write(BoardProtocol.WriteRegister(BoardProtocol.ExtensionInitAddress1, 0x55));
        connection.Write(BoardProtocol.WriteRegister(BoardProtocol.ExtensionInitAddress2, 0x00));

        var id = ReadRegister(connection, BoardProtocol.ExtensionIdAddress, 6);
        if (id is null || !id.AsSpan().SequenceEqual(BoardProtocol.BalanceBoardExtensionId))
        {
            return null; // Wii Remote など
        }

        var data = ReadRegister(connection, BoardProtocol.CalibrationAddress, BoardProtocol.CalibrationLength);
        var calibration = data is null ? null : BoardCalibration.TryParse(data);
        if (calibration is null)
        {
            throw new InvalidOperationException("Balance Board calibration data could not be read.");
        }

        connection.Write(BoardProtocol.SetLed(1));
        connection.Write(BoardProtocol.SetReportModeExtension8());
        connection.Write(BoardProtocol.StatusRequest());
        return calibration;
    }

    /// <summary>レジスタを読む。応答は 16 バイトずつの 0x21 に分かれて届く。失敗・タイムアウトは null。</summary>
    private byte[]? ReadRegister(IBoardConnection connection, int address, int length)
    {
        connection.Write(BoardProtocol.ReadRegister(address, length));

        var result = new byte[length];
        var received = 0;
        var buffer = new byte[connection.InputReportLength];
        var timer = Stopwatch.StartNew();
        while (received < length && timer.Elapsed < _options.ReadResponseTimeout)
        {
            var count = connection.Read(buffer);
            if (count == 0
                || !BoardReportParser.TryParseReadData(buffer.AsSpan(0, count), out var responseAddress, out var error, out var data))
            {
                continue; // 他のレポート(ステータス等)は読み捨てる
            }

            if (error != 0)
            {
                return null;
            }

            if (responseAddress != ((address + received) & 0xFFFF))
            {
                continue;
            }

            var take = Math.Min(data.Length, length - received);
            data[..take].CopyTo(result.AsSpan(received));
            received += take;
        }

        return received == length ? result : null;
    }

    private void ReadLoop(IBoardConnection connection, BoardCalibration calibration, CancellationToken token)
    {
        var buffer = new byte[connection.InputReportLength];
        var statusTimer = Stopwatch.StartNew();
        var connectedFor = Stopwatch.StartNew();
        var sinceLastReport = Stopwatch.StartNew();
        long frames = 0;
        Exception? failure = null;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var count = connection.Read(buffer);
                if (count > 0)
                {
                    var report = buffer.AsSpan(0, count);
                    sinceLastReport.Restart();
                    if (BoardReportParser.TryParseRawSensors(report, out var raw))
                    {
                        frames++;
                        RaiseFrame(calibration.ToKilograms(raw));
                    }
                    else if (BoardReportParser.TryParseStatus(report, out _, out var battery, out var batteryRaw, out var low))
                    {
                        // ステータスを受けるとレポートモードが戻ることがあるため、都度設定し直す。
                        connection.Write(BoardProtocol.SetReportModeExtension8());
                        UpdateBattery(battery, batteryRaw, low);
                    }
                }

                if (statusTimer.Elapsed >= _options.StatusInterval)
                {
                    statusTimer.Restart();
                    connection.Write(BoardProtocol.StatusRequest());
                }
            }
        }
        catch (Exception ex)
        {
            // 電源オフ・Bluetooth 切断・DisconnectAsync によるクローズ。
            failure = ex;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                // 利用者が切断したのではない(ボードの電源オフ・電波・OS の電源管理など)。原因調査のため、状況を残す。
                var reason = failure is null ? "read loop ended" : $"{failure.GetType().Name}: {failure.Message}";
                _log.Info(
                    $"Connection lost after {connectedFor.Elapsed:hh\\:mm\\:ss} " +
                    $"(battery {BatteryPercent}%, {frames} frames, last report {sinceLastReport.ElapsedMilliseconds} ms ago): {reason}");
                FinishDisconnected(connection);
            }
        }
    }

    private void RaiseFrame(SensorFrame frame)
    {
        try
        {
            FrameReceived?.Invoke(this, frame);
        }
        catch
        {
            // 購読者の不具合でデバイスの読み取りを止めない。
        }
    }

    private void UpdateBattery(int percent, int raw, bool low)
    {
        bool rawChanged, lowChanged, percentChanged;
        lock (_stateGate)
        {
            rawChanged = _batteryRaw != raw;
            lowChanged = _batteryLow != low;
            percentChanged = _batteryPercent != percent;
            _batteryRaw = raw;
            _batteryLow = low;
            _batteryPercent = percent;
        }

        // 実機での電池表示の精度を後から検証できるよう、生の値が変わるたびに残す(ステータスは約 60 秒ごと)。
        if (rawChanged || lowChanged)
        {
            _log.Info($"Battery raw={raw} ({percent}%){(low ? " LOW" : string.Empty)}");
        }

        if (lowChanged && low)
        {
            _log.Info("Battery is low - replace the Balance Board batteries.");
        }

        if (percentChanged || lowChanged)
        {
            BatteryChanged?.Invoke(this, percent);
        }
    }

    /// <summary>
    /// 切断状態へ遷移する。<paramref name="connection"/> が現在の接続でなければ(別の処理が片付け済み)何もしない。
    /// null は接続を持たない状態からの遷移(接続失敗・スキャン中断)。
    /// </summary>
    private void FinishDisconnected(IBoardConnection? connection)
    {
        lock (_stateGate)
        {
            if (connection is not null && !ReferenceEquals(_connection, connection))
            {
                return;
            }

            _connection?.Dispose();
            _connection = null;
            _batteryPercent = 0;
            _batteryRaw = -1;
            _batteryLow = false;
        }

        SetState(ConnectionState.Disconnected);
    }

    private void SetState(ConnectionState state)
    {
        lock (_stateGate)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
        }

        ConnectionStateChanged?.Invoke(this, state);
    }
}
