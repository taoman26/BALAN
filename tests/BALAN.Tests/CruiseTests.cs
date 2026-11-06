using BALAN.Core.Cruise;
using BALAN.Core.Domain;
using BALAN.Core.Engine;
using BALAN.Core.Settings;
using Xunit;

namespace BALAN.Tests;

public class CruiseControllerTests
{
    private static readonly BalanSettings S = new() { MaxSpeed = 20, BrakeStrength = 0.6, BrakeLoadKg = 4.0 }; // 2 / 5 kg、ブレーキ 4 kg

    private static PedalForces Down(double kg) => new(0, kg, 0);
    private static PedalForces Up(double kg) => new(kg, 0, 0);

    private static CruiseController Run(PedalForces f, double seconds, CruiseController? c = null)
    {
        c ??= new CruiseController();
        for (var t = 0.0; t < seconds; t += 0.01) c.Update(f, true, 0.01, S);
        return c;
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.99)]
    [InlineData(2.0)]
    public void UpToTwoKgNothingHappens(double kg)
    {
        var c = Run(Down(kg), 2);
        Assert.Equal(0, c.Speed);
        Assert.Equal(CruiseLevel.Stopped, c.Level);
        Assert.Equal(ScrollDirection.None, c.Direction);
    }

    [Theory]
    [InlineData(2.5, CruiseLevel.Slow)]
    [InlineData(3.5, CruiseLevel.Medium)]
    [InlineData(4.5, CruiseLevel.Fast)]
    [InlineData(5.0, CruiseLevel.Fast)]
    [InlineData(9.0, CruiseLevel.Fast)]
    public void LevelsFollowTwoThreeFourKg(double kg, CruiseLevel expected)
    {
        Assert.Equal(expected, Run(Down(kg), 3).Level);
    }

    [Fact]
    public void LevelBoundariesAreExactlyTwoThreeFour()
    {
        Assert.Equal(CruiseLevel.Stopped, CruiseController.LevelFor(1.99, S));
        Assert.Equal(CruiseLevel.Slow, CruiseController.LevelFor(2.0, S));
        Assert.Equal(CruiseLevel.Slow, CruiseController.LevelFor(2.99, S));
        Assert.Equal(CruiseLevel.Medium, CruiseController.LevelFor(3.0, S));
        Assert.Equal(CruiseLevel.Medium, CruiseController.LevelFor(3.99, S));
        Assert.Equal(CruiseLevel.Fast, CruiseController.LevelFor(4.0, S));
    }

    [Fact]
    public void SpeedRisesSmoothlyAndMonotonicallyWithLoad()
    {
        var previous = 0.0;
        var maxStep = 0.0;
        for (var kg = 1.9; kg <= 5.0; kg += 0.1)
        {
            var speed = Run(Down(kg), 2).Speed;
            Assert.True(speed >= previous, $"speed dropped at {kg} kg");
            maxStep = Math.Max(maxStep, speed - previous);
            previous = speed;
        }

        Assert.Equal(20, previous, 6);      // 5 kg で最大
        Assert.True(maxStep < 20 * 0.35);   // 0.1 kg あたりの増加が小さい = 階段状に跳ばない
    }

    [Fact]
    public void SpeedFollowsSquareCurveBetweenDeadZoneAndFullLoad()
    {
        Assert.Equal(0.25 * 20, Run(Down(3.5), 3).Speed, 6); // depth 0.5 → 0.25
    }

    [Fact]
    public void BiggerOfTrAndBrWinsAndSetsDirection()
    {
        Assert.Equal(ScrollDirection.Down, Run(new PedalForces(3, 4, 0), 1).Direction);
        Assert.Equal(ScrollDirection.Up, Run(new PedalForces(4, 3, 0), 1).Direction);
        Assert.Equal(Run(Down(4), 3).Speed, Run(new PedalForces(3, 4, 0), 3).Speed, 6); // 勝った側の荷重で速度が決まる
    }

    [Fact]
    public void LighterSideBelowDeadZoneIsIgnored()
    {
        Assert.Equal(ScrollDirection.None, Run(new PedalForces(1.5, 1.0, 0), 2).Direction);
    }

    [Fact]
    public void BrakeNeedsFourKgOnTlOrBl()
    {
        var below = Run(new PedalForces(0, 5, 3.9), 2);
        Assert.False(below.IsBraking);
        Assert.True(below.Speed > 0);

        var at = Run(new PedalForces(0, 5, 4.0), 2);
        Assert.True(at.IsBraking);
        Assert.Equal(0, at.Speed);
    }

    [Fact]
    public void BrakeStopsQuicklyAndOverridesAccelerator()
    {
        var c = Run(Down(5), 2);
        Run(new PedalForces(0, 5, 4.5), 0.2, c); // アクセルを踏んだままブレーキ
        Assert.Equal(0, c.Speed);
        Assert.Equal(ScrollDirection.None, c.Direction);
    }

    [Fact]
    public void ReleasingStopsQuickly()
    {
        var c = Run(Down(5), 2);
        Run(Down(0), 0.3, c);
        Assert.Equal(0, c.Speed);
    }

    [Fact]
    public void AccelerationIsGradual()
    {
        var c = new CruiseController();
        c.Update(Down(5), true, 0.01, S);
        Assert.InRange(c.Speed, 0.001, 20 * 0.1);
    }

    [Fact]
    public void ReversingPassesThroughStop()
    {
        var c = Run(Down(5), 2);
        c.Update(Up(5), true, 0.01, S);
        Assert.Equal(ScrollDirection.Down, c.Direction); // 逆走せず、まず減速
        Run(Up(5), 3, c);
        Assert.Equal(ScrollDirection.Up, c.Direction);
        Assert.Equal(20, c.Speed, 6);
    }

    [Fact]
    public void NoDataStopsImmediately()
    {
        var c = Run(Down(5), 2);
        c.Update(Down(5), false, 0.01, S);
        Assert.Equal(0, c.Speed);
    }

    [Fact]
    public void HugeDtIsClamped()
    {
        var c = new CruiseController();
        c.Update(Down(5), true, 5.0, S);
        Assert.True(c.Speed <= 20 * 0.2 + 1e-9);
    }

    [Fact]
    public void NaNInputStops()
    {
        var c = Run(Down(5), 2);
        c.Update(new PedalForces(double.NaN, 0, 0), true, 0.01, S);
        Assert.Equal(0, c.Speed);
    }
}

public class EngineTests
{
    private static (BalanEngine engine, FakeClock clock, RecordingSender sender, SettingsService settings) Create()
    {
        var settings = new SettingsService(new MemoryStore { Value = new BalanSettings { MaxSpeed = 10, BrakeLoadKg = 4.0 } });
        var clock = new FakeClock();
        var sender = new RecordingSender();
        return (new BalanEngine(settings, sender, clock), clock, sender, settings);
    }

    /// <summary>10 ms ごとにフレームを入れて Tick する。</summary>
    private static void Drive(BalanEngine engine, FakeClock clock, SensorFrame frame, double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += 0.01)
        {
            clock.Advance(0.01);
            engine.OnFrame(frame);
            engine.Tick();
        }
    }

    private static SensorFrame Br(double kg) => new(0, 0, 0, kg);
    private static SensorFrame Tr(double kg) => new(0, kg, 0, 0);

    [Fact]
    public void BrScrollsDownWithNegativeWheel()
    {
        var (e, clock, sender, _) = Create();
        Drive(e, clock, Br(5), 2);
        Assert.True(sender.Total < 0);
        Assert.All(sender.Sent, d => Assert.True(d < 0 && d % 30 == 0));
        Assert.Equal(CruiseLevel.Fast, e.Snapshot.Level);
        Assert.Equal(ScrollDirection.Down, e.Snapshot.Direction);
    }

    [Fact]
    public void TrScrollsUpWithPositiveWheel()
    {
        var (e, clock, sender, _) = Create();
        Drive(e, clock, Tr(5), 2);
        Assert.True(sender.Total > 0);
        Assert.All(sender.Sent, d => Assert.True(d > 0));
        Assert.Equal(ScrollDirection.Up, e.Snapshot.Direction);
    }

    private static (BalanEngine engine, FakeClock clock, RecordingSender sender, SettingsService settings) CreateHorizontal()
    {
        var r = Create();
        r.settings.Update(c => c with { ScrollMode = ScrollMode.Horizontal });
        return r;
    }

    [Theory]
    [InlineData(0, 5, 0, 0)] // TR
    [InlineData(0, 0, 0, 5)] // BR
    public void HorizontalRightFootScrollsRight(double tl, double tr, double bl, double br)
    {
        var (e, clock, sender, _) = CreateHorizontal();
        Drive(e, clock, new SensorFrame(tl, tr, bl, br), 2);
        Assert.True(sender.HorizontalTotal > 0);
        Assert.Empty(sender.Sent); // 垂直には一切送らない
        Assert.Equal(ScrollDirection.Right, e.Snapshot.Direction);
        Assert.Equal(CruiseLevel.Fast, e.Snapshot.Level);
    }

    [Theory]
    [InlineData(5, 0, 0, 0)] // TL
    [InlineData(0, 0, 5, 0)] // BL
    public void HorizontalLeftFootScrollsLeftWithoutBraking(double tl, double tr, double bl, double br)
    {
        var (e, clock, sender, _) = CreateHorizontal();
        Drive(e, clock, new SensorFrame(tl, tr, bl, br), 2);
        Assert.True(sender.HorizontalTotal < 0);
        Assert.Empty(sender.Sent);
        Assert.Equal(ScrollDirection.Left, e.Snapshot.Direction);
        Assert.False(e.Snapshot.IsBraking); // 5kg は垂直ならブレーキ閾値だが、水平ではブレーキにならない
    }

    [Fact]
    public void HorizontalIdleDoesNothingAndStopsWhenReleased()
    {
        var (e, clock, sender, _) = CreateHorizontal();
        Drive(e, clock, new SensorFrame(1, 1, 1, 1), 1);
        Assert.Empty(sender.SentHorizontal);
        Drive(e, clock, new SensorFrame(0, 5, 0, 0), 1);
        Assert.True(e.Snapshot.Speed > 0);
        Drive(e, clock, new SensorFrame(0, 0, 0, 0), 1);
        Assert.Equal(0, e.Snapshot.Speed);
    }

    [Fact]
    public void HorizontalUsesSameSpeedCurveAsVertical()
    {
        var (v, vc, vs, _) = Create();
        var (h, hc, hs, _) = CreateHorizontal();
        Drive(v, vc, Tr(3.5), 3);
        Drive(h, hc, new SensorFrame(0, 3.5, 0, 0), 3);
        Assert.Equal(v.Snapshot.Speed, h.Snapshot.Speed, 6);
        Assert.Equal(vs.Total, hs.HorizontalTotal);
    }

    [Fact]
    public void SwitchingModeStopsAndRoutesToNewAxis()
    {
        var (e, clock, sender, settings) = Create();
        Drive(e, clock, Tr(5), 1);
        var vertical = sender.Total;
        Assert.True(vertical > 0);
        settings.Update(c => c with { ScrollMode = ScrollMode.Horizontal });
        Drive(e, clock, new SensorFrame(0, 0, 0, 0), 0.1);
        Assert.Equal(0, e.Snapshot.Speed);
        Drive(e, clock, new SensorFrame(0, 5, 0, 0), 1);
        Assert.Equal(vertical, sender.Total); // 垂直はもう増えない
        Assert.True(sender.HorizontalTotal > 0);
    }

    [Fact]
    public void LargerOfTrAndBrDecidesDirection()
    {
        var (e, clock, sender, _) = Create();
        Drive(e, clock, new SensorFrame(0, 3, 0, 4.5), 2);
        Assert.True(sender.Total < 0);
    }

    [Fact]
    public void TotalScrolledMatchesSpeedTimesTime()
    {
        var (e, clock, sender, _) = Create();
        Drive(e, clock, Br(5), 1);
        sender.Sent.Clear();
        Drive(e, clock, Br(5), 2); // 10 notch/s × 2 s = 20 notch = 2400
        Assert.InRange(-sender.Total, 2400 - 30, 2400 + 30);
    }

    [Fact]
    public void LightLoadNeverScrolls()
    {
        var (e, clock, sender, _) = Create();
        Drive(e, clock, new SensorFrame(1.0, 1.9, 1.0, 1.9), 3);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void SingleSpikeIsDamped()
    {
        var (e, clock, sender, _) = Create();
        Drive(e, clock, Br(0.5), 1);
        clock.Advance(0.01);
        e.OnFrame(Br(6)); // 1 フレームだけの跳ね
        e.Tick();
        Drive(e, clock, Br(0.5), 0.5);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void BrakeOnBlStopsEvenWhileHoldingBr()
    {
        var (e, clock, sender, _) = Create();
        Drive(e, clock, Br(5), 2);
        Drive(e, clock, new SensorFrame(0, 0, 4.5, 5), 0.5);
        sender.Sent.Clear();
        Drive(e, clock, new SensorFrame(0, 0, 4.5, 5), 1);
        Assert.Empty(sender.Sent);
        Assert.True(e.Snapshot.IsBraking);
    }

    [Fact]
    public void BrakeNeedsFourKg()
    {
        var (e, clock, sender, _) = Create();
        Drive(e, clock, new SensorFrame(3.5, 0, 0, 5), 2);
        Assert.False(e.Snapshot.IsBraking);
        Assert.True(sender.Total < 0);
    }

    [Fact]
    public void StaleSensorDataStops()
    {
        var (e, clock, sender, _) = Create();
        Drive(e, clock, Br(5), 2);
        sender.Sent.Clear();
        for (var i = 0; i < 100; i++) { clock.Advance(0.01); e.Tick(); } // フレームが来ない(切断・電池切れ)
        Assert.True(sender.Sent.Count <= 31);
        var before = sender.Sent.Count;
        for (var i = 0; i < 100; i++) { clock.Advance(0.01); e.Tick(); }
        Assert.Equal(before, sender.Sent.Count);
        Assert.Equal(0, e.Snapshot.Speed);
    }

    [Fact]
    public void ScrollDisabledShowsButDoesNotSend()
    {
        var (e, clock, sender, settings) = Create();
        settings.Update(s => s with { ScrollEnabled = false });
        Drive(e, clock, Br(5), 2);
        Assert.Empty(sender.Sent);
        Assert.Equal(CruiseLevel.Fast, e.Snapshot.Level);
    }

    [Fact]
    public async Task CalibrationStoresRestingLoadAsTare()
    {
        var (e, clock, _, settings) = Create();
        var task = e.CalibrateAsync();
        Drive(e, clock, new SensorFrame(0.4, 0.6, 0.5, 1.2), 2.0); // 足を載せているだけで BR に 1.2 kg
        Assert.Equal(CalibrationResult.Completed, await task);
        Assert.Equal(1.2, settings.Current.TareBottomRight, 3);
        Assert.Equal(0.4, settings.Current.TareTopLeft, 3);

        Drive(e, clock, new SensorFrame(0.4, 0.6, 0.5, 1.2), 1.0); // 載せているだけなら 0 kg 扱い
        Assert.Equal(0, e.Snapshot.ForceDown, 3);
        Drive(e, clock, Br(1.2 + 3.5), 3.0);                        // そこから 3.5 kg 押し込めば Medium
        Assert.Equal(CruiseLevel.Medium, e.Snapshot.Level);
    }

    [Fact]
    public async Task CalibrationWithoutDataDoesNotChangeTare()
    {
        var (e, clock, _, settings) = Create();
        var task = e.CalibrateAsync();
        for (var i = 0; i < 200; i++) { clock.Advance(0.01); e.Tick(); } // センサー入力なし
        Assert.Equal(CalibrationResult.NoData, await task);
        Assert.Equal(0, settings.Current.TareBottomRight);
    }

    [Fact]
    public void NoScrollWhileCalibrating()
    {
        var (e, clock, sender, _) = Create();
        _ = e.CalibrateAsync();
        Drive(e, clock, Br(5), 1.0);
        Assert.Empty(sender.Sent);
        Assert.True(e.IsCalibrating);
    }

    [Fact]
    public void SenderFailureDoesNotBreakLoop()
    {
        var clock = new FakeClock();
        var e = new BalanEngine(new SettingsService(new MemoryStore { Value = new BalanSettings { MaxSpeed = 10, BrakeLoadKg = 4.0 } }), new ThrowingSender(), clock);
        Drive(e, clock, Br(5), 1);
        Assert.Equal(CruiseLevel.Fast, e.Snapshot.Level);
    }

    private sealed class ThrowingSender : BALAN.Core.Abstractions.IScrollSender
    {
        public void SendVertical(int wheelDelta) => throw new InvalidOperationException();

        public void SendHorizontal(int wheelDelta) => throw new InvalidOperationException();
    }
}

public class DefaultSettingsTests
{
    [Fact]
    public void DefaultsMatchFieldTunedValues()
    {
        var s = new BalanSettings();
        Assert.Equal(2.0, s.DeadZoneKg);
        Assert.Equal(5.0, s.FullLoadKg);
        Assert.Equal(5.0, s.BrakeLoadKg);
        Assert.Equal(ScrollMode.Vertical, s.ScrollMode);
    }

    [Fact]
    public void ScrollModeRoundTripsAndOldFilesDefaultToVertical()
    {
        var path = Path.Combine(Path.GetTempPath(), $"balan-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new BalanSettings { ScrollMode = ScrollMode.Horizontal });
            Assert.Contains("Horizontal", File.ReadAllText(path));
            Assert.Equal(ScrollMode.Horizontal, store.Load().ScrollMode);

            File.WriteAllText(path, "{ \"DeadZoneKg\": 2.5 }"); // 旧バージョンの設定
            Assert.Equal(ScrollMode.Vertical, store.Load().ScrollMode);

            File.WriteAllText(path, "{ \"ScrollMode\": 99 }");
            Assert.Equal(ScrollMode.Vertical, store.Load().ScrollMode);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
