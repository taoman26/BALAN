using Avalonia.Controls;
using BALAN.Core.Domain;
using BALAN.UI.Tray;
using Xunit;

namespace BALAN.Tests;

public class TrayPolicyTests
{
    [Theory]
    [InlineData(WindowCloseReason.WindowClosing, false, true)]   // ユーザーが × を押した → 非表示
    [InlineData(WindowCloseReason.WindowClosing, true, false)]   // Exit 中 → 閉じる
    [InlineData(WindowCloseReason.ApplicationShutdown, false, false)]
    [InlineData(WindowCloseReason.OSShutdown, false, false)]     // OS のシャットダウンを妨げない
    [InlineData(WindowCloseReason.OwnerWindowClosing, false, false)]
    public void CloseIsReplacedByHideOnlyWhenTheUserClosesTheWindow(WindowCloseReason reason, bool exiting, bool expected)
    {
        Assert.Equal(expected, TrayPolicy.ShouldHideInsteadOfClose(reason, exiting));
    }

    [Theory]
    [InlineData(WindowState.Minimized, false, true)]
    [InlineData(WindowState.Minimized, true, false)]
    [InlineData(WindowState.Normal, false, false)]
    [InlineData(WindowState.Maximized, false, false)]
    public void MinimizeIsReplacedByHide(WindowState state, bool exiting, bool expected)
    {
        Assert.Equal(expected, TrayPolicy.ShouldHideOnStateChange(state, exiting));
    }

    [Theory]
    [InlineData(ConnectionState.Connected, "icon_tray_connected.png")]
    [InlineData(ConnectionState.Connecting, "icon_tray_connecting.png")]
    [InlineData(ConnectionState.Disconnected, "icon_tray_disconnected.png")]
    public void IconFollowsConnectionState(ConnectionState state, string file)
    {
        Assert.Equal($"/Assets/{file}", TrayPolicy.IconFor(state).AbsolutePath);
    }
}
