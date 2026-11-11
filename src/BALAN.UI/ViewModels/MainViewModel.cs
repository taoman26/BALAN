using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using BALAN.Core.Abstractions;
using BALAN.Core.Domain;
using BALAN.Core.Engine;
using BALAN.Core.Settings;
using ReactiveUI;

namespace BALAN.UI.ViewModels;

/// <summary>メイン画面。エンジンの状態を表示し、感度設定とキャリブレーション・接続を操作する。</summary>
public sealed class MainViewModel : ReactiveObject, IDisposable
{
    private static readonly TimeSpan UiRefresh = TimeSpan.FromMilliseconds(33);

    private readonly IBalanceBoard _board;
    private readonly BalanEngine _engine;
    private readonly SettingsService _settings;
    private readonly CompositeDisposable _subscriptions = new();

    private ConnectionState _state;
    private int _batteryPercent;
    private bool _batteryLow;
    private EngineSnapshot _snapshot = EngineSnapshot.Idle;
    private double _deadZone; // kg
    private double _brakeLoad; // kg
    private double _maxSpeed;
    private double _brakeStrength;
    private bool _scrollEnabled;
    private bool _horizontal;
    private bool _isCalibrating;
    private string? _message;

    public MainViewModel(IBalanceBoard board, BalanEngine engine, SettingsService settings, MessageLog? log = null)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        var s = settings.Current;
        _deadZone = s.DeadZoneKg;
        _brakeLoad = s.BrakeLoadKg;
        _maxSpeed = s.MaxSpeed;
        _brakeStrength = s.BrakeStrength;
        _scrollEnabled = s.ScrollEnabled;
        _horizontal = s.ScrollMode == ScrollMode.Horizontal;
        _state = board.State;
        _batteryPercent = board.BatteryPercent;
        _batteryLow = board.BatteryLow;

        // 画面 → 設定(即時に巡航へ反映)。保存はまとめて遅延させる。
        _subscriptions.Add(this.WhenAnyValue(x => x.DeadZone, x => x.MaxSpeed, x => x.BrakeStrength, x => x.ScrollEnabled, x => x.BrakeLoadKg, x => x.IsHorizontal)
            .Skip(1)
            .Subscribe(t => _settings.Update(c => c with
            {
                DeadZoneKg = t.Item1,
                MaxSpeed = t.Item2,
                BrakeStrength = t.Item3,
                ScrollEnabled = t.Item4,
                BrakeLoadKg = t.Item5,
                ScrollMode = t.Item6 ? ScrollMode.Horizontal : ScrollMode.Vertical
            })));

        // 設定 → 保存(Phase 7)。スライダーを動かしている間は書き込まず、止めて 0.5 秒後に 1 回だけ保存する。
        _subscriptions.Add(Observable
            .FromEventPattern<EventHandler<BalanSettings>, BalanSettings>(h => settings.Changed += h, h => settings.Changed -= h)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ => TrySave()));

        // エンジン → 画面(100 Hz の更新を 30 fps 程度に間引き、UI スレッドで反映)。
        _subscriptions.Add(Observable
            .FromEventPattern<EventHandler<EngineSnapshot>, EngineSnapshot>(h => engine.SnapshotChanged += h, h => engine.SnapshotChanged -= h)
            .Select(e => e.EventArgs)
            .Sample(UiRefresh)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(snap => Snapshot = snap));

        _subscriptions.Add(Observable
            .FromEventPattern<EventHandler<ConnectionState>, ConnectionState>(h => board.ConnectionStateChanged += h, h => board.ConnectionStateChanged -= h)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(e =>
            {
                State = e.EventArgs;
                if (e.EventArgs == ConnectionState.Connected)
                {
                    Message = null;
                }
            }));

        _subscriptions.Add(Observable
            .FromEventPattern<EventHandler<int>, int>(h => board.BatteryChanged += h, h => board.BatteryChanged -= h)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(e =>
            {
                BatteryPercent = e.EventArgs;
                BatteryLow = board.BatteryLow;
            }));

        // 送出失敗(権限不足など)とペアリングの経過を、フッターに出す。
        _subscriptions.Add(Observable
            .FromEventPattern<EventHandler<string>, string>(h => engine.ScrollFailed += h, h => engine.ScrollFailed -= h)
            .Select(e => $"Cannot send scroll: {e.EventArgs}")
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(m => Message = m));
        if (log is not null)
        {
            _subscriptions.Add(Observable
                .FromEvent<Action<string>, string>(h => h, h => log.Logged += h, h => log.Logged -= h)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(m => Message = m));
        }

        ConnectCommand = ReactiveCommand.CreateFromTask(ToggleConnectionAsync);
        CalibrateCommand = ReactiveCommand.CreateFromTask(CalibrateAsync);
        ShowWindowCommand = ReactiveCommand.Create(() => ShowWindowRequested?.Invoke(this, EventArgs.Empty));
        MinimizeToTrayCommand = ReactiveCommand.Create(() => MinimizeRequested?.Invoke(this, EventArgs.Empty));
        ExitCommand = ReactiveCommand.Create(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        ToggleScrollModeCommand = ReactiveCommand.Create(() => { IsHorizontal = !IsHorizontal; });

        // コマンド内の予期しない例外でアプリを落とさず、画面に出す。
        foreach (var thrown in new[] { ConnectCommand.ThrownExceptions, CalibrateCommand.ThrownExceptions })
        {
            _subscriptions.Add(thrown.ObserveOn(RxApp.MainThreadScheduler).Subscribe(ex =>
            {
                IsCalibrating = false;
                Message = $"Error: {ex.Message}";
            }));
        }
    }

    // ----- 接続 -----

    public ConnectionState State
    {
        get => _state;
        private set
        {
            this.RaiseAndSetIfChanged(ref _state, value);
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(ConnectButtonText));
            this.RaisePropertyChanged(nameof(FooterText));
            this.RaisePropertyChanged(nameof(TrayToolTip));
        }
    }

    public bool IsConnected => _state == ConnectionState.Connected;

    public string StatusText => _state switch
    {
        ConnectionState.Connected => "Connected",
        ConnectionState.Connecting => "Connecting...",
        _ => "Disconnected",
    };

    public string ConnectButtonText => _state == ConnectionState.Disconnected ? "Connect" : "Disconnect";

    public int BatteryPercent
    {
        get => _batteryPercent;
        private set
        {
            this.RaiseAndSetIfChanged(ref _batteryPercent, value);
            this.RaisePropertyChanged(nameof(BatteryText));
        }
    }

    /// <summary>ボード自身が「電池残量わずか」と報告している。</summary>
    public bool BatteryLow
    {
        get => _batteryLow;
        private set
        {
            this.RaiseAndSetIfChanged(ref _batteryLow, value);
            this.RaisePropertyChanged(nameof(BatteryText));
        }
    }

    public string BatteryText => !IsConnected || _batteryPercent <= 0 ? string.Empty
        : _batteryLow ? $"Battery LOW ({_batteryPercent}%)"
        : $"Battery {_batteryPercent}%";

    // ----- 荷重・重心・巡航 -----

    public EngineSnapshot Snapshot
    {
        get => _snapshot;
        private set
        {
            this.RaiseAndSetIfChanged(ref _snapshot, value);
            foreach (var name in new[]
            {
                nameof(TopLeftKg), nameof(TopRightKg), nameof(BottomLeftKg), nameof(BottomRightKg), nameof(TotalKg), nameof(TotalText),
                nameof(ForceUp), nameof(ForceDown), nameof(ForceBrake), nameof(HasData), nameof(Speed), nameof(SpeedText),
                nameof(Level), nameof(LevelText), nameof(IsStopped), nameof(IsSlow), nameof(IsMedium), nameof(IsFast),
                nameof(StateDetail), nameof(TrayToolTip)
            })
            {
                this.RaisePropertyChanged(name);
            }
        }
    }

    public double TopLeftKg => _snapshot.Frame?.TopLeft ?? 0;
    public double TopRightKg => _snapshot.Frame?.TopRight ?? 0;
    public double BottomLeftKg => _snapshot.Frame?.BottomLeft ?? 0;
    public double BottomRightKg => _snapshot.Frame?.BottomRight ?? 0;
    public double TotalKg => _snapshot.Frame?.Total ?? 0;
    public string TotalText => _snapshot.Frame is null ? "--" : $"{TotalKg:0.0}";

    /// <summary>TR の荷重(kg)= 上スクロールのアクセル。</summary>
    public double ForceUp => _snapshot.ForceUp;

    /// <summary>BR の荷重(kg)= 下スクロールのアクセル。</summary>
    public double ForceDown => _snapshot.ForceDown;

    /// <summary>TL / BL の大きい方(kg)= ブレーキ。</summary>
    public double ForceBrake => _snapshot.ForceBrake;

    public bool HasData => _snapshot.HasData;

    /// <summary>荷重メーターの目盛り上限(kg)。</summary>
    public double MeterMaxKg => Math.Max(_settings.Current.FullLoadKg, _brakeLoad) * 1.25;

    /// <summary>TL / BL のどちらかがこの荷重(kg)以上でブレーキが効く。</summary>
    public double BrakeLoadKg
    {
        get => _brakeLoad;
        set
        {
            this.RaiseAndSetIfChanged(ref _brakeLoad, Math.Round(value, 1));
            this.RaisePropertyChanged(nameof(MeterMaxKg));
        }
    }

    public double Speed => _snapshot.Speed;
    public string SpeedText => $"{Speed:0.0}";
    public CruiseLevel Level => _snapshot.Level;
    public string LevelText => _snapshot.Level.ToString();
    public bool IsStopped => Level == CruiseLevel.Stopped;
    public bool IsSlow => Level == CruiseLevel.Slow;
    public bool IsMedium => Level == CruiseLevel.Medium;
    public bool IsFast => Level == CruiseLevel.Fast;

    /// <summary>状態の補足説明(何が起きているかを一言で)。</summary>
    public string StateDetail =>
        !IsConnected ? "Waiting for Balance Board"
        : !HasData ? "No sensor data"
        : _snapshot.IsBraking ? "Braking"
        : Speed > 0 ? $"Scrolling {DirectionText}{(_scrollEnabled ? string.Empty : "  (output off)")}"
        : "Idle  -  nothing happens";

    private string DirectionText => _snapshot.Direction switch
    {
        ScrollDirection.Up => "up",
        ScrollDirection.Down => "down",
        ScrollDirection.Left => "left",
        ScrollDirection.Right => "right",
        _ => string.Empty,
    };

    // ----- 設定 -----

    /// <summary>Scroll Mode。true = 水平(チルト)、false = 垂直(巡航)。</summary>
    public bool IsHorizontal
    {
        get => _horizontal;
        set
        {
            if (_horizontal == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _horizontal, value);
            this.RaisePropertyChanged(nameof(IsVertical));
            this.RaisePropertyChanged(nameof(ScrollModeMenuText));
            this.RaisePropertyChanged(nameof(PositiveLabel));
            this.RaisePropertyChanged(nameof(NegativeLabel));
        }
    }

    /// <summary>トレイメニューの項目名。現在のモードを示し、クリックでもう一方へ切り替わる。</summary>
    public string ScrollModeMenuText => _horizontal ? "Scroll Mode: Horizontal  (click to switch)" : "Scroll Mode: Vertical  (click to switch)";

    public bool IsVertical
    {
        get => !_horizontal;
        set => IsHorizontal = !value;
    }

    /// <summary>PEDALS の 1 行目・2 行目の見出し(方式で変わる)。3 行目のブレーキは垂直のみ。</summary>
    public string PositiveLabel => _horizontal ? "TR/BR  ▶ RIGHT" : "TR  ▲ UP";
    public string NegativeLabel => _horizontal ? "TL/BL  ◀ LEFT" : "BR  ▼ DOWN";

    public double DeadZone
    {
        get => _deadZone;
        set => this.RaiseAndSetIfChanged(ref _deadZone, Math.Round(value, 1));
    }

    public double MaxSpeed
    {
        get => _maxSpeed;
        set => this.RaiseAndSetIfChanged(ref _maxSpeed, Math.Round(value));
    }

    public double BrakeStrength
    {
        get => _brakeStrength;
        set => this.RaiseAndSetIfChanged(ref _brakeStrength, Math.Round(value, 2));
    }

    public bool ScrollEnabled
    {
        get => _scrollEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _scrollEnabled, value);
            this.RaisePropertyChanged(nameof(StateDetail));
        }
    }

    public bool IsCalibrating
    {
        get => _isCalibrating;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isCalibrating, value);
            this.RaisePropertyChanged(nameof(CalibrateButtonText));
        }
    }

    public string CalibrateButtonText => _isCalibrating ? "Measuring..." : "Calibrate";

    // ----- メッセージ -----

    public string? Message
    {
        get => _message;
        private set
        {
            this.RaiseAndSetIfChanged(ref _message, value);
            this.RaisePropertyChanged(nameof(FooterText));
        }
    }

    public string FooterText => _message ?? (_state switch
    {
        ConnectionState.Connected => "Running in system tray  -  closing this window keeps BALAN active",
        ConnectionState.Connecting => "Press the red SYNC button on the Balance Board",
        _ => "Press Connect, then the red SYNC button on the Balance Board",
    });

    public string TrayToolTip => IsConnected
        ? $"BALAN - {LevelText}{(Speed > 0 ? $" {DirectionText} ({Speed:0.0}/s)" : string.Empty)}"
        : $"BALAN - {StatusText}";

    // ----- コマンド・要求 -----

    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }
    public ReactiveCommand<Unit, Unit> CalibrateCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowWindowCommand { get; }
    public ReactiveCommand<Unit, Unit> MinimizeToTrayCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    /// <summary>Scroll Mode を Vertical ⇄ Horizontal で切り替える(トレイメニュー用)。</summary>
    public ReactiveCommand<Unit, Unit> ToggleScrollModeCommand { get; }

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? ExitRequested;

    /// <summary>起動時の自動接続。失敗してもメッセージを出すだけ(Connect ボタンでやり直せる)。</summary>
    public Task InitializeAsync() => _state == ConnectionState.Disconnected ? ToggleConnectionAsync() : Task.CompletedTask;

    /// <summary>終了時: 設定を保存し、切断する。</summary>
    public async Task ShutdownAsync()
    {
        TrySave();
        try
        {
            await _board.DisconnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // 終了処理の失敗で終了を妨げない。
        }
    }

    public void Dispose() => _subscriptions.Dispose();

    private async Task ToggleConnectionAsync()
    {
        if (_board.State != ConnectionState.Disconnected)
        {
            await _board.DisconnectAsync();
            return;
        }

        Message = null;
        try
        {
            await _board.ConnectAsync();
        }
        catch (TimeoutException ex)
        {
            Message = ex.Message;
        }
        catch (Exception ex)
        {
            Message = $"Could not connect: {ex.Message}";
        }
    }

    private async Task CalibrateAsync()
    {
        if (!IsConnected || !HasData)
        {
            Message = "Connect the board first, then press Calibrate.";
            return;
        }

        IsCalibrating = true;
        Message = "Rest your feet on the board without pressing, and keep still...";
        try
        {
            var result = await _engine.CalibrateAsync().ConfigureAwait(true);
            Message = result switch
            {
                CalibrationResult.Completed => "Calibrated. Resting load is now treated as zero.",
                CalibrationResult.NoData => "Calibration failed: no sensor data. Try again.",
                _ => "Calibration cancelled.",
            };
        }
        finally
        {
            IsCalibrating = false;
        }
    }

    private void TrySave()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"Could not save settings: {ex.Message}";
        }
    }
}
