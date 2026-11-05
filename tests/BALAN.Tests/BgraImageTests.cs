using BALAN.UI.Tray.X11;
using Xunit;

namespace BALAN.Tests;

public class BgraImageTests
{
    private static BgraImage Solid(int w, int h, byte b, byte g, byte r, byte a)
    {
        var pixels = new byte[w * h * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }

        return new BgraImage(w, h, pixels);
    }

    [Fact]
    public void Constructor_RejectsMismatchedBuffers()
    {
        Assert.Throws<ArgumentException>(() => new BgraImage(2, 2, new byte[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BgraImage(0, 2, Array.Empty<byte>()));
        Assert.Throws<ArgumentNullException>(() => new BgraImage(1, 1, null!));
    }

    [Fact]
    public void ScaledTo_SameSize_ReturnsTheSameImage()
    {
        var image = Solid(4, 4, 1, 2, 3, 255);

        Assert.Same(image, image.ScaledTo(4, 4));
    }

    [Fact]
    public void ScaledTo_SolidColor_KeepsTheColor()
    {
        var scaled = Solid(128, 128, 10, 20, 30, 255).ScaledTo(24, 24);

        Assert.Equal(24, scaled.Width);
        Assert.Equal(24, scaled.Pixels.Length / 24 / 4);
        for (var i = 0; i < scaled.Pixels.Length; i += 4)
        {
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, scaled.Pixels[i..(i + 4)]);
        }
    }

    [Fact]
    public void ScaledTo_AveragesAndDoesNotLetTransparentPixelsTintTheColor()
    {
        // 左 1 画素は不透明な赤、右 1 画素は完全に透明(色は緑だが α=0 なので結果に混ざってはいけない)。
        var pixels = new byte[] { 0, 0, 255, 255, 0, 255, 0, 0 };
        var scaled = new BgraImage(2, 1, pixels).ScaledTo(1, 1);

        Assert.Equal(new byte[] { 0, 0, 255 }, scaled.Pixels[..3]);
        Assert.InRange(scaled.Pixels[3], 127, 128); // α は平均で約半分
    }

    [Fact]
    public void ScaledTo_CanEnlarge()
    {
        var scaled = Solid(2, 2, 5, 6, 7, 255).ScaledTo(8, 8);

        Assert.Equal(8 * 8 * 4, scaled.Pixels.Length);
        Assert.Equal(new byte[] { 5, 6, 7, 255 }, scaled.Pixels[..4]);
    }

    [Fact]
    public void ToShapeMask_PacksBitsLeastSignificantFirst_WithRowPadding()
    {
        // 10x2: 1 行目は 0,2,9 列目が不透明。2 行目はすべて透明。
        var pixels = new byte[10 * 2 * 4];
        foreach (var x in new[] { 0, 2, 9 })
        {
            pixels[x * 4 + 3] = 255;
        }

        var mask = new BgraImage(10, 2, pixels).ToShapeMask();

        Assert.Equal(new byte[] { 0b0000_0101, 0b0000_0010, 0, 0 }, mask);
    }

    [Fact]
    public void ToShapeMask_UsesTheThreshold()
    {
        var pixels = new byte[] { 0, 0, 0, 127, 0, 0, 0, 128 };

        Assert.Equal(new byte[] { 0b10 }, new BgraImage(2, 1, pixels).ToShapeMask(128));
    }
}
