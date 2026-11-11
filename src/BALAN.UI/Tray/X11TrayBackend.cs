using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using BALAN.Core.Domain;
using BALAN.UI.Tray.X11;
using BALAN.UI.ViewModels;

namespace BALAN.UI.Tray;

/// <summary>
/// XEmbed 方式のトレイ(Xfce の通知領域、Enlightenment の Systray など従来型)に表示する。
/// 左クリックでウィンドウ表示、右クリックでメニュー(<see cref="TrayMenuWindow"/>)。ツールチップは非対応。
/// </summary>
public sealed class X11TrayBackend : ITrayBackend
{
    private readonly X11SystemTray _tray = new();
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<ConnectionState, BgraImage> _images = new();
    private TrayMenuWindow? _menu;

    private X11TrayBackend(MainViewModel viewModel) => _viewModel = viewModel;

    public string Name => "XEmbed";

    /// <summary>トレイへの登録に成功すれば作成して返す。トレイ管理者が無い等で使えなければ null。</summary>
    public static X11TrayBackend? TryCreate(MainViewModel viewModel)
    {
        try
        {
            var backend = new X11TrayBackend(viewModel);
            backend._tray.LeftClicked += (_, _) =>
                Dispatcher.UIThread.Post(() => viewModel.ShowWindowCommand.Execute().Subscribe());
            backend._tray.MenuRequested += (_, p) =>
                Dispatcher.UIThread.Post(() => backend.ShowMenu(new PixelPoint(p.X, p.Y)));

            if (backend._tray.Start(backend.ImageFor(viewModel.IndicatorState)))
            {
                return backend;
            }

            backend._tray.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public void SetState(ConnectionState state) => _tray.SetImage(ImageFor(state));

    public void SetToolTip(string text)
    {
        // XEmbed のトレイにはツールチップの仕組みが無い。
    }

    public void Dispose()
    {
        Dispatcher.UIThread.Post(() => _menu?.Close());
        _tray.Dispose();
    }

    private void ShowMenu(PixelPoint cursor)
    {
        _menu?.Close();
        _menu = new TrayMenuWindow(_viewModel);
        _menu.ShowAt(cursor);
    }

    private BgraImage ImageFor(ConnectionState state)
    {
        if (!_images.TryGetValue(state, out var image))
        {
            image = Load(TrayPolicy.IconFor(state));
            _images[state] = image;
        }

        return image;
    }

    /// <summary>PNG を B,G,R,A(ストレート α)の生ピクセルに展開する。</summary>
    internal static BgraImage Load(Uri uri)
    {
        using var stream = AssetLoader.Open(uri);
        using var bitmap = new Bitmap(stream);
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        // R,G,B,A の並びなら B,G,R,A に入れ替える。
        if (bitmap.Format == PixelFormat.Rgba8888)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            }
        }

        // Avalonia のビットマップは α 乗算済みのことがあるので、ストレートに戻す。
        if (bitmap.AlphaFormat == AlphaFormat.Premul)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var a = pixels[i + 3];
                if (a is > 0 and < 255)
                {
                    pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                    pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                    pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
                }
            }
        }

        return new BgraImage(size.Width, size.Height, pixels);
    }
}
