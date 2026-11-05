using System.Runtime.InteropServices;

namespace BALAN.UI.Tray.X11;

/// <summary>libX11 / libXext の P/Invoke(XEmbed システムトレイ用の最小限)。LP64(64bit Linux)前提。</summary>
internal static class X11Native
{
    private const string X11 = "libX11.so.6";
    private const string XExt = "libXext.so.6";

    public const int ClientMessage = 33;
    public const int ButtonPress = 4;
    public const int Expose = 12;
    public const int DestroyNotify = 17;
    public const int ConfigureNotify = 22;

    public const long ButtonPressMask = 1L << 2;
    public const long ExposureMask = 1L << 15;
    public const long StructureNotifyMask = 1L << 17;

    public const int PropModeReplace = 0;
    public const int ZPixmap = 2;
    public const nuint ParentRelative = 1;
    public const int ShapeBounding = 0;
    public const int ShapeSet = 0;

    /// <summary>XEvent の大きさ(LP64 で 192 バイト)。</summary>
    public const int XEventSize = 192;

    [DllImport(X11)]
    public static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport(X11)]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport(X11)]
    public static extern int XDefaultScreen(IntPtr display);

    [DllImport(X11)]
    public static extern nuint XRootWindow(IntPtr display, int screen);

    [DllImport(X11)]
    public static extern nuint XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [DllImport(X11)]
    public static extern nuint XGetSelectionOwner(IntPtr display, nuint selection);

    [DllImport(X11)]
    public static extern nuint XCreateSimpleWindow(
        IntPtr display, nuint parent, int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);

    [DllImport(X11)]
    public static extern int XDestroyWindow(IntPtr display, nuint window);

    [DllImport(X11)]
    public static extern int XSelectInput(IntPtr display, nuint window, nint eventMask);

    [DllImport(X11)]
    public static extern int XChangeProperty(
        IntPtr display, nuint window, nuint property, nuint type, int format, int mode, nint[] data, int elements);

    [DllImport(X11)]
    public static extern int XChangeProperty(
        IntPtr display, nuint window, nuint property, nuint type, int format, int mode, byte[] data, int elements);

    [DllImport(X11)]
    public static extern int XSetWindowBackgroundPixmap(IntPtr display, nuint window, nuint pixmap);

    [DllImport(X11)]
    public static extern int XSendEvent(IntPtr display, nuint window, [MarshalAs(UnmanagedType.Bool)] bool propagate, nint eventMask, IntPtr xevent);

    [DllImport(X11)]
    public static extern int XFlush(IntPtr display);

    [DllImport(X11)]
    public static extern int XPending(IntPtr display);

    [DllImport(X11)]
    public static extern int XNextEvent(IntPtr display, IntPtr xevent);

    [DllImport(X11)]
    public static extern IntPtr XDefaultVisual(IntPtr display, int screen);

    [DllImport(X11)]
    public static extern int XDefaultDepth(IntPtr display, int screen);

    [DllImport(X11)]
    public static extern IntPtr XCreateGC(IntPtr display, nuint drawable, nuint valueMask, IntPtr values);

    [DllImport(X11)]
    public static extern int XFreeGC(IntPtr display, IntPtr gc);

    [DllImport(X11)]
    public static extern IntPtr XCreateImage(
        IntPtr display, IntPtr visual, uint depth, int format, int offset, IntPtr data, uint width, uint height, int bitmapPad, int bytesPerLine);

    /// <summary>XImage を破棄する。<c>data</c> も Xfree(=free)されるため、data は malloc で確保したものを渡すこと。</summary>
    [DllImport(X11)]
    public static extern int XDestroyImage(IntPtr image);

    [DllImport(X11)]
    public static extern int XPutImage(
        IntPtr display, nuint drawable, IntPtr gc, IntPtr image, int srcX, int srcY, int dstX, int dstY, uint width, uint height);

    [DllImport(X11)]
    public static extern nuint XCreateBitmapFromData(IntPtr display, nuint drawable, byte[] data, uint width, uint height);

    [DllImport(X11)]
    public static extern int XFreePixmap(IntPtr display, nuint pixmap);

    [DllImport(X11)]
    public static extern int XClearWindow(IntPtr display, nuint window);

    [DllImport(XExt)]
    public static extern void XShapeCombineMask(IntPtr display, nuint dest, int destKind, int xOffset, int yOffset, nuint source, int op);
}
