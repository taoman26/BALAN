using BALAN.Core.Abstractions;
using BALAN.Core.Domain;

namespace BALAN.Device;

/// <summary>
/// 実機なしで画面と巡航制御を確かめるための模擬ボード(<c>--simulate</c>)。
/// 約 24 秒周期で「無操作 → BR を踏む(下へ Slow→Medium→Fast)→ 抜く → TR を踏む(上へ)→ BR を踏みながら BL でブレーキ」を繰り返す。
/// </summary>
public sealed class SimulatedBalanceBoard : IBalanceBoard, IAsyncDisposable
{
    private readonly object _gate = new();
    private ConnectionState _state = ConnectionState.Disconnected;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public ConnectionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool BatteryLow => false;

    public bool SensorsFrozen => false;

    public event EventHandler<bool>? SensorsFrozenChanged { add { } remove { } }

    public int BatteryPercent => State == ConnectionState.Connected ? 82 : 0;

    public event EventHandler<SensorFrame>? FrameReceived;

    public event EventHandler<ConnectionState>? ConnectionStateChanged;

    public event EventHandler<int>? BatteryChanged;

    public async Task ConnectAsync()
    {
        lock (_gate)
        {
            if (_state != ConnectionState.Disconnected)
            {
                return;
            }

            _state = ConnectionState.Connecting;
        }

        ConnectionStateChanged?.Invoke(this, ConnectionState.Connecting);
        await Task.Delay(500).ConfigureAwait(false);

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _cts = cts;
            _state = ConnectionState.Connected;
        }

        ConnectionStateChanged?.Invoke(this, ConnectionState.Connected);
        BatteryChanged?.Invoke(this, 82);
        _loop = Task.Run(() => Run(cts.Token));
    }

    public async Task DisconnectAsync()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
        }

        cts?.Cancel();
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
            _loop = null;
        }

        lock (_gate)
        {
            _state = ConnectionState.Disconnected;
        }

        ConnectionStateChanged?.Invoke(this, ConnectionState.Disconnected);
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());

    /// <summary>周期内の時刻(秒)から、座って足で操作しているときの荷重(kg)を返す。</summary>
    internal static SensorFrame FrameAt(double seconds)
    {
        var t = seconds % 24.0;
        double tl = 0.3, tr = 0.3, bl = 0.3, br = 0.3; // 足を載せているだけ
        if (t >= 3 && t < 9) br = 5.0 * (t - 3) / 6;            // 下へ: 2 kg で Slow、3 kg で Medium、4 kg 以上で Fast
        else if (t >= 9 && t < 11) br = 5.0 * (11 - t) / 2;     // 抜く
        else if (t >= 12 && t < 18) tr = 5.0 * (t - 12) / 6;    // 上へ
        else if (t >= 18 && t < 20) tr = 5.0 * (20 - t) / 2;
        else if (t >= 20 && t < 23)
        {
            br = 4.5;                                           // 下へ進みながら…
            if (t >= 21.5) bl = 5.0;                            // 左(BL)を踏んでブレーキ
        }

        return new SensorFrame(tl, tr, bl, br);
    }

    private void Run(CancellationToken token)
    {
        var start = System.Diagnostics.Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        try
        {
            while (timer.WaitForNextTickAsync(token).AsTask().GetAwaiter().GetResult())
            {
                FrameReceived?.Invoke(this, FrameAt(start.Elapsed.TotalSeconds));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
