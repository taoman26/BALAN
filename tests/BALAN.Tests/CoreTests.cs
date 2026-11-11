using BALAN.Core.Cruise;
using BALAN.Core.Domain;
using BALAN.Core.Engine;
using BALAN.Core.Settings;
using Xunit;

namespace BALAN.Tests;

public class ExponentialFilterTests
{
    [Fact]
    public void FirstSampleIsAdoptedAsIs()
    {
        var f = new ExponentialFilter();
        Assert.Equal(0.5, f.Add(0.5));
    }

    [Fact]
    public void FollowsEightyTwentyRule()
    {
        var f = new ExponentialFilter();
        f.Add(0.0);
        Assert.Equal(0.2, f.Add(1.0), 10); // 0.0 * 0.8 + 1.0 * 0.2
        Assert.Equal(0.2 * 0.8 + 0.2, f.Add(1.0), 10);
    }

    [Fact]
    public void SingleSpikeIsDamped()
    {
        var f = new ExponentialFilter();
        for (var i = 0; i < 20; i++) f.Add(0);
        Assert.True(f.Add(1.0) <= 0.2 + 1e-9);
    }
}

public class SettingsTests
{
    [Fact]
    public void NormalizeClampsOutOfRangeValues()
    {
        var s = new BalanSettings { DeadZoneKg = 50, FullLoadKg = 1, MaxSpeed = -3, BrakeStrength = double.NaN, TareBottomRight = -4 }.Normalize();
        Assert.Equal(6, s.DeadZoneKg);
        Assert.True(s.FullLoadKg >= s.DeadZoneKg + 0.5);
        Assert.Equal(1, s.MaxSpeed);
        Assert.Equal(0.6, s.BrakeStrength);
        Assert.Equal(0, s.TareBottomRight);
    }

    [Fact]
    public void JsonRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "balan-test-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new BalanSettings { DeadZoneKg = 2.5, MaxSpeed = 30, TareBottomRight = 0.7 });
            var loaded = store.Load();
            Assert.Equal(2.5, loaded.DeadZoneKg);
            Assert.Equal(30, loaded.MaxSpeed);
            Assert.Equal(0.7, loaded.TareBottomRight);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void MissingOrCorruptFileFallsBackToDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "balan-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            Assert.Equal(new BalanSettings(), new JsonSettingsStore(path).Load());
            File.WriteAllText(path, "{ not json");
            Assert.Equal(new BalanSettings(), new JsonSettingsStore(path).Load());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ServiceRaisesChangedAndPersists()
    {
        var store = new MemoryStore();
        var svc = new SettingsService(store);
        BalanSettings? seen = null;
        svc.Changed += (_, s) => seen = s;
        svc.Update(s => s with { DeadZoneKg = 3.0 });
        svc.Save();
        Assert.Equal(3.0, seen!.DeadZoneKg);
        Assert.Equal(1, store.Saves);
        Assert.Equal(3.0, store.Value.DeadZoneKg);
    }
}

public class BridgeTests
{
    private sealed class FakeBoard : BALAN.Core.Abstractions.IBalanceBoard
    {
        public ConnectionState State { get; private set; }
        public int BatteryPercent => 0;
        public bool BatteryLow => false;
        public bool SensorsFrozen => false;
        public event EventHandler<bool>? SensorsFrozenChanged { add { } remove { } }
        public event EventHandler<SensorFrame>? FrameReceived;
        public event EventHandler<ConnectionState>? ConnectionStateChanged;
        public event EventHandler<int>? BatteryChanged { add { } remove { } }
        public Task ConnectAsync() => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public void Frame(SensorFrame f) => FrameReceived?.Invoke(this, f);
        public void Set(ConnectionState s) { State = s; ConnectionStateChanged?.Invoke(this, s); }
    }

    [Fact]
    public void DisconnectResetsEngineToStopped()
    {
        var clock = new FakeClock();
        var engine = new BalanEngine(new SettingsService(new MemoryStore { Value = new BalanSettings() }), new RecordingSender(), clock);
        var board = new FakeBoard();
        using var bridge = new BoardEngineBridge(board, engine);

        for (var i = 0; i < 200; i++) { clock.Advance(0.01); board.Frame(new SensorFrame(0, 0, 0, 5)); engine.Tick(); }
        Assert.Equal(CruiseLevel.Fast, engine.Snapshot.Level);

        board.Set(ConnectionState.Disconnected);
        Assert.Equal(CruiseLevel.Stopped, engine.Snapshot.Level);
        Assert.Null(engine.Snapshot.Frame);
    }
}
