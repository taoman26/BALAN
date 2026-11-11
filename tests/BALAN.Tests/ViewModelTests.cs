using BALAN.Core.Abstractions;
using BALAN.Core.Domain;
using BALAN.Core.Engine;
using BALAN.Core.Settings;
using BALAN.UI.ViewModels;
using Xunit;

namespace BALAN.Tests;

public class ScrollModeToggleTests
{
    private sealed class IdleBoard : IBalanceBoard
    {
        public ConnectionState State => ConnectionState.Disconnected;
        public int BatteryPercent => 0;
        public bool BatteryLow => false;
        public event EventHandler<SensorFrame>? FrameReceived { add { } remove { } }
        public event EventHandler<ConnectionState>? ConnectionStateChanged { add { } remove { } }
        public event EventHandler<int>? BatteryChanged { add { } remove { } }
        public Task ConnectAsync() => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
    }

    private sealed class BatteryBoard : IBalanceBoard
    {
        public ConnectionState State => ConnectionState.Connected;
        public int BatteryPercent => 98;
        public bool BatteryLow { get; set; }
        public event EventHandler<SensorFrame>? FrameReceived { add { } remove { } }
        public event EventHandler<ConnectionState>? ConnectionStateChanged { add { } remove { } }
        public event EventHandler<int>? BatteryChanged { add { } remove { } }
        public Task ConnectAsync() => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
    }

    private static MainViewModel Create(IBalanceBoard board)
    {
        var settings = new SettingsService(new MemoryStore());
        return new MainViewModel(board, new BalanEngine(settings, new RecordingSender(), new FakeClock()), settings);
    }

    [Fact]
    public void BatteryIsHiddenUnlessTheBoardReportsLow()
    {
        using var normal = Create(new BatteryBoard()); // 残量の%(98)は表示しない
        Assert.Equal(string.Empty, normal.BatteryText);

        using var low = Create(new BatteryBoard { BatteryLow = true });
        Assert.Contains("Battery LOW", low.BatteryText);
        Assert.DoesNotContain("%", low.BatteryText);
    }

    [Fact]
    public void TrayToggleSwitchesModeAndUpdatesSettingsAndMenuText()
    {
        var settings = new SettingsService(new MemoryStore());
        using var vm = new MainViewModel(new IdleBoard(), new BalanEngine(settings, new RecordingSender(), new FakeClock()), settings);
        Assert.Equal(ScrollMode.Vertical, settings.Current.ScrollMode);
        Assert.Contains("Vertical", vm.ScrollModeMenuText);

        vm.ToggleScrollModeCommand.Execute().Subscribe();
        Assert.Equal(ScrollMode.Horizontal, settings.Current.ScrollMode);
        Assert.Contains("Horizontal", vm.ScrollModeMenuText);
        Assert.False(vm.IsVertical);

        vm.ToggleScrollModeCommand.Execute().Subscribe();
        Assert.Equal(ScrollMode.Vertical, settings.Current.ScrollMode);
        Assert.Contains("Vertical", vm.ScrollModeMenuText);
    }
}
