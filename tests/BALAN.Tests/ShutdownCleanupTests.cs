using System.Diagnostics;
using BALAN.UI;
using Xunit;

namespace BALAN.Tests;

public class ShutdownCleanupTests
{
    [Fact]
    public void RunsCleanupSynchronously()
    {
        var done = false;
        Assert.True(ShutdownCleanup.Run(async () => { await Task.Delay(50); done = true; }, TimeSpan.FromSeconds(2)));
        Assert.True(done); // 戻った時点で後始末は終わっている(終了を後回しにしない)
    }

    [Fact]
    public void FailureDoesNotThrow()
    {
        Assert.False(ShutdownCleanup.Run(() => throw new InvalidOperationException(), TimeSpan.FromSeconds(1)));
        Assert.False(ShutdownCleanup.Run(async () => { await Task.Yield(); throw new IOException(); }, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void HangingCleanupIsAbandonedAfterTimeout()
    {
        var sw = Stopwatch.StartNew();
        Assert.False(ShutdownCleanup.Run(() => Task.Delay(TimeSpan.FromSeconds(30)), TimeSpan.FromMilliseconds(200)));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }
}
