namespace BALAN.UI.Tray.X11;

/// <summary>B,G,R,A の順(ストレート α)の 32bit 画像。X11 の ZPixmap(深度 24/32)にそのまま渡せる並び。</summary>
public sealed class BgraImage
{
    public BgraImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image size must be positive.");
        }

        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentException("Pixel buffer size does not match the image size.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    /// <summary>面積平均で縮小/拡大する(トレイの大きさに合わせるため)。α で重み付けして、透明部分の色が混ざらないようにする。</summary>
    public BgraImage ScaledTo(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return this;
        }

        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var y0 = (double)y * Height / height;
            var y1 = (double)(y + 1) * Height / height;
            for (var x = 0; x < width; x++)
            {
                var x0 = (double)x * Width / width;
                var x1 = (double)(x + 1) * Width / width;

                double b = 0, g = 0, r = 0, a = 0, weightSum = 0;
                for (var sy = (int)Math.Floor(y0); sy < Math.Min(Height, (int)Math.Ceiling(y1)); sy++)
                {
                    var wy = Math.Min(sy + 1, y1) - Math.Max(sy, y0);
                    for (var sx = (int)Math.Floor(x0); sx < Math.Min(Width, (int)Math.Ceiling(x1)); sx++)
                    {
                        var w = wy * (Math.Min(sx + 1, x1) - Math.Max(sx, x0));
                        var i = (sy * Width + sx) * 4;
                        var alpha = Pixels[i + 3];
                        b += Pixels[i] * alpha * w;
                        g += Pixels[i + 1] * alpha * w;
                        r += Pixels[i + 2] * alpha * w;
                        a += alpha * w;
                        weightSum += w;
                    }
                }

                var o = (y * width + x) * 4;
                if (a > 0 && weightSum > 0)
                {
                    result[o] = (byte)Math.Round(b / a);
                    result[o + 1] = (byte)Math.Round(g / a);
                    result[o + 2] = (byte)Math.Round(r / a);
                    result[o + 3] = (byte)Math.Round(a / weightSum);
                }
            }
        }

        return new BgraImage(width, height, result);
    }

    /// <summary>
    /// 1bit の形状マスク(X11 のビットマップ形式: 1 行をバイト境界に詰め、各バイトは下位ビットが左)。
    /// α が閾値以上のピクセルを 1(表示)にする。角の丸い透明部分を、トレイの背景色で塗らずに抜くために使う。
    /// </summary>
    public byte[] ToShapeMask(byte alphaThreshold = 128)
    {
        var rowBytes = (Width + 7) / 8;
        var mask = new byte[rowBytes * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (Pixels[(y * Width + x) * 4 + 3] >= alphaThreshold)
                {
                    mask[y * rowBytes + x / 8] |= (byte)(1 << (x % 8));
                }
            }
        }

        return mask;
    }
}
