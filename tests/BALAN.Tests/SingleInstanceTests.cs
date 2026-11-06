using BALAN.App;
using Xunit;

namespace BALAN.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void SecondInstanceIsRejectedUntilFirstReleases()
    {
        var path = Path.Combine(Path.GetTempPath(), $"balan-{Guid.NewGuid():N}", "balan.lock");
        try
        {
            var first = SingleInstance.TryAcquire(path, out var running1);
            Assert.NotNull(first);
            Assert.False(running1);

            var second = SingleInstance.TryAcquire(path, out var running2);
            Assert.Null(second);
            Assert.True(running2);

            first!.Dispose();
            using var third = SingleInstance.TryAcquire(path, out var running3);
            Assert.NotNull(third); // 終了(解放)後は起動できる
            Assert.False(running3);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
