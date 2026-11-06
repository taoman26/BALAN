using BALAN.Core.Abstractions;
using BALAN.Core.Cruise;
using BALAN.Core.Domain;
using BALAN.Core.Settings;

namespace BALAN.Core.Engine;

public enum CalibrationResult
{
    Completed,

    /// <summary>測定中にセンサー入力が途切れた/届かなかった。</summary>
    NoData,

    /// <summary>センサー入力が途切れた/中断された。</summary>
    Aborted
}

/// <summary>
/// 荷重 → 平滑化 → 巡航制御 → スクロール送出 を結ぶ中核。
/// センサー入力(<see cref="OnFrame"/>、読み取りスレッド)と、一定周期の <see cref="Tick"/>(タイマー)を分離している。
/// センサーが途絶えた場合(切断・電池切れ)は、スクロールが止まらなくならないよう必ず停止する。
/// </summary>
public sealed class BalanEngine
{
    /// <summary>この時間センサー入力が無ければ「誰も乗っていない」扱いにする。</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(300);

    public static readonly TimeSpan CalibrationDuration = TimeSpan.FromMilliseconds(1500);

    private readonly SettingsService _settings;
    private readonly IScrollSender _sender;
    private readonly IClock _clock;
    private readonly CruiseController _cruise = new();
    private readonly ExponentialFilter[] _filters = { new(0.2), new(0.2), new(0.2), new(0.2) }; // TL, TR, BL, BR
    private readonly object _gate = new();

    private SensorFrame? _frame;
    private TimeSpan _frameAt = TimeSpan.MinValue;
    private bool _filterNeedsReset = true;
    private TimeSpan _lastTick;
    private double _wheelRemainder;
    private ScrollMode _mode = ScrollMode.Vertical;

    private TaskCompletionSource<CalibrationResult>? _calibration;
    private TimeSpan _calibrationEnd;
    private readonly double[] _calibrationSum = new double[4];
    private int _calibrationCount;
    private bool _calibrationLostData;

    private EngineSnapshot _snapshot = EngineSnapshot.Idle;

    // キャリブレーションの結果。ロックの外で設定へ反映し、待機側を完了させる(ロック保持中にコールバックを呼ばない)。
    private TaskCompletionSource<CalibrationResult>? _finishedCalibration;
    private CalibrationResult _finishedResult;
    private double[]? _finishedTare;

    public BalanEngine(SettingsService settings, IScrollSender sender, IClock? clock = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _clock = clock ?? new SystemClock();
        _lastTick = _clock.Now;
    }

    public EngineSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public bool IsCalibrating
    {
        get
        {
            lock (_gate)
            {
                return _calibration is not null;
            }
        }
    }

    /// <summary>スクロールの送出に失敗したとき(同じ内容は 1 度だけ)。引数は理由。</summary>
    public event EventHandler<string>? ScrollFailed;

    private string? _lastSendError;

    /// <summary>周期ごとに更新された状態を通知する(Tick を呼んだスレッドから)。</summary>
    public event EventHandler<EngineSnapshot>? SnapshotChanged;

    /// <summary>デバイスから荷重を受け取る。各センサーを指数移動平均で平滑化する。</summary>
    public void OnFrame(SensorFrame frame)
    {
        lock (_gate)
        {
            _frame = frame;
            _frameAt = _clock.Now;
            if (_filterNeedsReset)
            {
                foreach (var f in _filters) f.Reset();
                _filterNeedsReset = false;
            }

            _filters[0].Add(frame.TopLeft);
            _filters[1].Add(frame.TopRight);
            _filters[2].Add(frame.BottomLeft);
            _filters[3].Add(frame.BottomRight);
        }
    }

    /// <summary>切断時など。速度を 0 に戻し、保持している荷重を捨てる。</summary>
    public void Reset()
    {
        TaskCompletionSource<CalibrationResult>? aborted;
        lock (_gate)
        {
            _frame = null;
            _filterNeedsReset = true;
            _cruise.Reset();
            _wheelRemainder = 0;
            _snapshot = EngineSnapshot.Idle;
            FinishCalibration(CalibrationResult.Aborted);
            aborted = _finishedCalibration;
            _finishedCalibration = null;
            _finishedTare = null;
        }

        aborted?.TrySetResult(CalibrationResult.Aborted);
        SnapshotChanged?.Invoke(this, EngineSnapshot.Idle);
    }

    /// <summary>
    /// キャリブレーション開始。<see cref="CalibrationDuration"/> の間、足を載せただけ(操作していない)の各センサー荷重を平均し、無操作の基準にする。
    /// </summary>
    public Task<CalibrationResult> CalibrateAsync()
    {
        lock (_gate)
        {
            if (_calibration is { } running)
            {
                return running.Task;
            }

            _calibration = new TaskCompletionSource<CalibrationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _calibrationEnd = _clock.Now + CalibrationDuration;
            Array.Clear(_calibrationSum);
            _calibrationCount = 0;
            _calibrationLostData = false;
            return _calibration.Task;
        }
    }

    /// <summary>1 周期(約 10〜16 ms ごと)。巡航を進め、スクロールを送出し、状態を通知する。</summary>
    public void Tick()
    {
        EngineSnapshot snapshot;
        int wheel = 0;
        var horizontal = false;
        TaskCompletionSource<CalibrationResult>? finished;
        CalibrationResult finishedResult;
        double[]? finishedTare;
        lock (_gate)
        {
            var now = _clock.Now;
            var dt = (now - _lastTick).TotalSeconds;
            _lastTick = now;
            var settings = _settings.Current;

            if (settings.ScrollMode != _mode)
            {
                // 方式を切り替えたら、前の方式の速度・端数を持ち越さない(別の軸へ流れ込まないように)。
                _mode = settings.ScrollMode;
                _cruise.Reset();
                _wheelRemainder = 0;
            }

            var hasData = _frame is not null && now - _frameAt <= StaleAfter;
            if (!hasData)
            {
                _filterNeedsReset = true; // 再開時に古い値から立ち上がらない
            }

            var smoothed = new SensorFrame(_filters[0].Value, _filters[1].Value, _filters[2].Value, _filters[3].Value);

            if (_calibration is not null)
            {
                RunCalibration(now, hasData, smoothed);
                // キャリブレーション中は動かさない(足を置いている最中の誤スクロール防止)。
                _cruise.Reset();
                _wheelRemainder = 0;
                snapshot = Build(hasData, PedalForces.Tare(smoothed, settings), settings.ScrollMode);
            }
            else
            {
                var tared = PedalForces.Tare(smoothed, settings);
                var forces = PedalForces.From(tared, settings.ScrollMode); // 荷重 → 速度の計算は両方式で共通
                _cruise.Update(forces, hasData, dt, settings);

                if (settings.ScrollEnabled && _cruise.Speed > 0)
                {
                    // 正方向(垂直=上 / 水平=右)= 正、負方向(下 / 左)= 負のホイール値。端数は持ち越して、低速でも滑らかに進める。
                    var sign = _cruise.Direction == ScrollDirection.Up ? 1 : -1;
                    _wheelRemainder += sign * _cruise.Speed * 120 * Math.Clamp(dt, 0, 0.1);
                    var quanta = (int)(Math.Abs(_wheelRemainder) / settings.WheelGranularity);
                    if (quanta > 0)
                    {
                        wheel = sign * quanta * settings.WheelGranularity;
                        _wheelRemainder -= wheel;
                        horizontal = settings.ScrollMode == ScrollMode.Horizontal;
                    }
                }
                else
                {
                    _wheelRemainder = 0;
                }

                snapshot = Build(hasData, tared, settings.ScrollMode);
            }

            _snapshot = snapshot;
            (finished, finishedResult, finishedTare) = (_finishedCalibration, _finishedResult, _finishedTare);
            _finishedCalibration = null;
            _finishedTare = null;
        }

        if (finished is not null)
        {
            try
            {
                if (finishedTare is { } t)
                {
                    _settings.Update(c => c with
                    {
                        TareTopLeft = t[0],
                        TareTopRight = t[1],
                        TareBottomLeft = t[2],
                        TareBottomRight = t[3],
                    });
                }
            }
            catch
            {
                finishedResult = CalibrationResult.Aborted;
            }

            finished.TrySetResult(finishedResult);
        }

        if (wheel != 0)
        {
            try
            {
                // 送出する軸だけが方式で分かれる。速度の計算は共通。
                if (horizontal)
                {
                    _sender.SendHorizontal(wheel);
                }
                else
                {
                    _sender.SendVertical(wheel);
                }
            }
            catch (Exception ex)
            {
                // OS への送出失敗(権限不足、UAC の昇格ウィンドウ前面など)で巡航ループを止めない。同じ内容は 1 度だけ通知する。
                if (ex.Message != _lastSendError)
                {
                    _lastSendError = ex.Message;
                    try
                    {
                        ScrollFailed?.Invoke(this, ex.Message);
                    }
                    catch
                    {
                    }
                }
            }
        }

        try
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
        catch
        {
            // 購読者(UI)の例外で巡航ループを止めない。
        }
    }

    private EngineSnapshot Build(bool hasData, SensorFrame tared, ScrollMode mode)
    {
        var f = PedalForces.From(tared, mode);
        var direction = mode == ScrollMode.Horizontal
            ? _cruise.Direction switch
            {
                ScrollDirection.Up => ScrollDirection.Right,
                ScrollDirection.Down => ScrollDirection.Left,
                var d => d,
            }
            : _cruise.Direction;
        return new EngineSnapshot(
            hasData ? tared : null, hasData, f.Up, f.Down, f.Brake,
            _cruise.Speed, direction, _cruise.Level, _cruise.IsBraking);
    }

    private void RunCalibration(TimeSpan now, bool hasData, SensorFrame smoothed)
    {
        if (hasData)
        {
            _calibrationSum[0] += smoothed.TopLeft;
            _calibrationSum[1] += smoothed.TopRight;
            _calibrationSum[2] += smoothed.BottomLeft;
            _calibrationSum[3] += smoothed.BottomRight;
            _calibrationCount++;
        }
        else
        {
            _calibrationLostData = true;
        }

        if (now < _calibrationEnd)
        {
            return;
        }

        // 測定中にセンサーが途切れた、またはサンプルが少なすぎる場合は採用しない。
        if (_calibrationLostData || _calibrationCount < 10)
        {
            FinishCalibration(CalibrationResult.NoData);
            return;
        }

        var n = _calibrationCount;
        _finishedTare = new[] { _calibrationSum[0] / n, _calibrationSum[1] / n, _calibrationSum[2] / n, _calibrationSum[3] / n };
        FinishCalibration(CalibrationResult.Completed);
    }

    /// <summary>ロック内で呼ぶ。待機側の完了は Tick(または Reset)がロックの外で行う。</summary>
    private void FinishCalibration(CalibrationResult result)
    {
        _finishedCalibration = _calibration;
        _finishedResult = result;
        _calibration = null;
    }
}
