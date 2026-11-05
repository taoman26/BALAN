using System.Runtime.InteropServices;

namespace BALAN.UI.Tray.X11;

/// <summary>
/// XEmbed 方式(_NET_SYSTEM_TRAY)のシステムトレイアイコン。Xfce・Enlightenment・KDE(X11)などの従来型トレイに表示される。
/// Avalonia 標準のトレイは DBus の StatusNotifierItem 方式のみで、対応プラグインの無いパネルには出ないため、これを併用する。
/// X11 の呼び出しは専用スレッドだけで行う(別の接続を使うので Avalonia の X11 とは干渉しない)。
/// </summary>
public sealed class X11SystemTray : IDisposable
{
    private const int DefaultSize = 24;

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private volatile bool _stopping;
    private volatile bool _available;
    private BgraImage? _source;
    private bool _dirty;

    /// <summary>左クリック。専用スレッドから発火する。</summary>
    public event EventHandler? LeftClicked;

    /// <summary>右(または中)クリック。引数は画面上のカーソル位置(x, y)。専用スレッドから発火する。</summary>
    public event EventHandler<(int X, int Y)>? MenuRequested;

    /// <summary>この環境で XEmbed のトレイ管理者が存在するか(Start せずに確かめる)。</summary>
    public static bool IsTrayManagerPresent()
    {
        try
        {
            var display = X11Native.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var screen = X11Native.XDefaultScreen(display);
                var selection = X11Native.XInternAtom(display, $"_NET_SYSTEM_TRAY_S{screen}", false);
                return X11Native.XGetSelectionOwner(display, selection) != 0;
            }
            finally
            {
                X11Native.XCloseDisplay(display);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>トレイへの登録を試みる。トレイ管理者が無い/X に接続できない場合は false。</summary>
    public bool Start(BgraImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _source = image;
        _dirty = true;

        _thread = new Thread(Run) { IsBackground = true, Name = "BALAN X11 tray" };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
        return _available;
    }

    /// <summary>アイコン画像を差し替える(接続状態の色の変更)。</summary>
    public void SetImage(BgraImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        lock (_gate)
        {
            _source = image;
            _dirty = true;
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        IntPtr display = IntPtr.Zero;
        var eventBuffer = Marshal.AllocHGlobal(X11Native.XEventSize);
        nuint window = 0;
        IntPtr gc = IntPtr.Zero;
        try
        {
            display = X11Native.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                return;
            }

            var screen = X11Native.XDefaultScreen(display);
            var root = X11Native.XRootWindow(display, screen);
            var trayAtom = X11Native.XInternAtom(display, $"_NET_SYSTEM_TRAY_S{screen}", false);
            var manager = X11Native.XGetSelectionOwner(display, trayAtom);
            if (manager == 0)
            {
                return; // トレイ管理者がいない。
            }

            window = X11Native.XCreateSimpleWindow(display, root, 0, 0, DefaultSize, DefaultSize, 0, 0, 0);
            X11Native.XSelectInput(display, window,
                (nint)(X11Native.ButtonPressMask | X11Native.ExposureMask | X11Native.StructureNotifyMask));
            X11Native.XSetWindowBackgroundPixmap(display, window, X11Native.ParentRelative);

            // XEmbed 情報(バージョン 0、フラグ 1 = マップ済み)。
            var xembedInfo = X11Native.XInternAtom(display, "_XEMBED_INFO", false);
            X11Native.XChangeProperty(display, window, xembedInfo, xembedInfo, 32, X11Native.PropModeReplace, new nint[] { 0, 1 }, 2);

            gc = X11Native.XCreateGC(display, window, 0, IntPtr.Zero);
            SendDockRequest(display, manager, window, eventBuffer);
            _available = true;
            _started.Set();

            int width = DefaultSize, height = DefaultSize;
            while (!_stopping)
            {
                while (X11Native.XPending(display) > 0)
                {
                    X11Native.XNextEvent(display, eventBuffer);
                    HandleEvent(eventBuffer, ref width, ref height);
                }

                BgraImage? image = null;
                lock (_gate)
                {
                    if (_dirty)
                    {
                        _dirty = false;
                        image = _source;
                    }
                }

                if (image is not null)
                {
                    Draw(display, window, gc, screen, image, width, height);
                }

                Thread.Sleep(30);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _available = false;
        }
        finally
        {
            if (display != IntPtr.Zero)
            {
                if (gc != IntPtr.Zero)
                {
                    X11Native.XFreeGC(display, gc);
                }

                if (window != 0)
                {
                    X11Native.XDestroyWindow(display, window);
                }

                X11Native.XFlush(display);
                X11Native.XCloseDisplay(display);
            }

            Marshal.FreeHGlobal(eventBuffer);
            _started.Set();
        }
    }

    private static void SendDockRequest(IntPtr display, nuint manager, nuint window, IntPtr buffer)
    {
        // XClientMessageEvent(LP64): type@0, window@32, message_type@40, format@48, data.l[5]@56。
        for (var i = 0; i < X11Native.XEventSize; i += 8)
        {
            Marshal.WriteInt64(buffer, i, 0);
        }

        Marshal.WriteInt32(buffer, 0, X11Native.ClientMessage);
        Marshal.WriteInt64(buffer, 32, (long)manager);
        Marshal.WriteInt64(buffer, 40, (long)X11Native.XInternAtom(display, "_NET_SYSTEM_TRAY_OPCODE", false));
        Marshal.WriteInt32(buffer, 48, 32);
        Marshal.WriteInt64(buffer, 56, 0);                 // CurrentTime
        Marshal.WriteInt64(buffer, 64, 0);                 // SYSTEM_TRAY_REQUEST_DOCK
        Marshal.WriteInt64(buffer, 72, (long)window);      // 埋め込むウィンドウ
        X11Native.XSendEvent(display, manager, false, (nint)X11Native.StructureNotifyMask, buffer);
        X11Native.XFlush(display);
    }

    private void HandleEvent(IntPtr ev, ref int width, ref int height)
    {
        switch (Marshal.ReadInt32(ev, 0))
        {
            case X11Native.ButtonPress:
            {
                // XButtonEvent(LP64): x_root@72, y_root@76, button@84。
                var xRoot = Marshal.ReadInt32(ev, 72);
                var yRoot = Marshal.ReadInt32(ev, 76);
                var button = Marshal.ReadInt32(ev, 84);
                if (button == 1)
                {
                    LeftClicked?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    MenuRequested?.Invoke(this, (xRoot, yRoot));
                }

                break;
            }

            case X11Native.ConfigureNotify:
                // XConfigureEvent(LP64): width@56, height@60。トレイが決めた大きさに合わせて描き直す。
                width = Math.Max(1, Marshal.ReadInt32(ev, 56));
                height = Math.Max(1, Marshal.ReadInt32(ev, 60));
                lock (_gate)
                {
                    _dirty = true;
                }

                break;

            case X11Native.Expose:
                lock (_gate)
                {
                    _dirty = true;
                }

                break;

            case X11Native.DestroyNotify:
                _stopping = true;
                break;
        }
    }

    private static void Draw(IntPtr display, nuint window, IntPtr gc, int screen, BgraImage source, int width, int height)
    {
        var size = Math.Max(1, Math.Min(width, height));
        var image = source.ScaledTo(size, size);

        // 透明部分は形状マスクで抜く(背景はトレイの色を透かす)。
        var maskData = image.ToShapeMask();
        var mask = X11Native.XCreateBitmapFromData(display, window, maskData, (uint)size, (uint)size);
        if (mask != 0)
        {
            try
            {
                X11Native.XShapeCombineMask(display, window, X11Native.ShapeBounding, 0, 0, mask, X11Native.ShapeSet);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // libXext が無ければ、透明部分は背景色で塗られるだけ。
            }
            finally
            {
                X11Native.XFreePixmap(display, mask);
            }
        }

        // XDestroyImage が data を free するため、malloc 由来(AllocHGlobal)の領域を渡す。
        var data = Marshal.AllocHGlobal(image.Pixels.Length);
        Marshal.Copy(image.Pixels, 0, data, image.Pixels.Length);
        var depth = X11Native.XDefaultDepth(display, screen);
        var ximage = X11Native.XCreateImage(
            display, X11Native.XDefaultVisual(display, screen), (uint)depth, X11Native.ZPixmap, 0, data, (uint)size, (uint)size, 32, 0);
        if (ximage == IntPtr.Zero)
        {
            Marshal.FreeHGlobal(data);
            return;
        }

        X11Native.XPutImage(display, window, gc, ximage, 0, 0, 0, 0, (uint)size, (uint)size);
        X11Native.XDestroyImage(ximage);
        X11Native.XFlush(display);
    }
}
