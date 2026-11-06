using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace BALAN.Input.Linux;

/// <summary>仮想入力デバイスへのイベント書き込み。テストで差し替えるための境界。</summary>
internal interface IInputEventSink : IDisposable
{
    /// <summary>入力イベントを 1 つ書き込む(SYN_REPORT は別途)。</summary>
    void Write(ushort type, ushort code, int value);
}

/// <summary>
/// /dev/uinput に仮想マウスを作る(KUNAI と同じ、uinput_user_dev による作成)。
/// ホイールだけのデバイスはポインタとして認識されないことがあるため、相対移動とボタンも宣言した通常のマウスとして作る(実際には動かさない)。
/// </summary>
internal sealed class UInputMouse : IInputEventSink
{
    private const string DevicePath = "/dev/uinput";

    // _IOW('U', 100, int) = EVBIT, 101 = KEYBIT, 102 = RELBIT / _IO('U', 1) / _IO('U', 2)
    private const uint UiSetEvBit = 0x40045564;
    private const uint UiSetKeyBit = 0x40045565;
    private const uint UiSetRelBit = 0x40045566;
    private const uint UiDevCreate = 0x5501;
    private const uint UiDevDestroy = 0x5502;

    public const ushort EvSyn = 0x00;
    public const ushort EvKey = 0x01;
    public const ushort EvRel = 0x02;
    public const ushort RelX = 0x00;
    public const ushort RelY = 0x01;
    public const ushort RelHWheel = 0x06;
    public const ushort RelWheel = 0x08;
    public const ushort RelWheelHiRes = 0x0B;
    public const ushort RelHWheelHiRes = 0x0C;
    public const ushort BtnLeft = 0x110;
    public const ushort BtnRight = 0x111;
    public const ushort BtnMiddle = 0x112;

    private const int BusUsb = 0x03;
    private const int UinputMaxNameSize = 80;
    private const int AbsCnt = 64;

    private readonly int _fd;
    private bool _disposed;

    private UInputMouse(int fd) => _fd = fd;

    /// <summary>権限が無い場合は、設定手順つきの <see cref="IOException"/>。</summary>
    public static UInputMouse Create(string deviceName)
    {
        var fd = LibC.open(DevicePath, LibC.ORdWr | LibC.ONonBlock);
        if (fd < 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            throw new IOException(
                $"Cannot open {DevicePath} ({new Win32Exception(errno).Message}). " +
                "Install tools/linux/99-balan.rules into /etc/udev/rules.d/, add your user to the 'input' group " +
                "(then log in again). If the module is missing, run: sudo modprobe uinput");
        }

        try
        {
            Ioctl(fd, UiSetEvBit, EvKey);
            Ioctl(fd, UiSetEvBit, EvRel);
            Ioctl(fd, UiSetEvBit, EvSyn);
            foreach (var key in new[] { BtnLeft, BtnRight, BtnMiddle })
            {
                Ioctl(fd, UiSetKeyBit, key);
            }

            foreach (var rel in new[] { RelX, RelY, RelWheel, RelWheelHiRes, RelHWheel, RelHWheelHiRes })
            {
                Ioctl(fd, UiSetRelBit, rel);
            }

            var setup = BuildUserDev(deviceName);
            if (LibC.write(fd, setup, (nuint)setup.Length) != setup.Length)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to write uinput device setup.");
            }

            Ioctl(fd, UiDevCreate, 0);
        }
        catch
        {
            LibC.close(fd);
            throw;
        }

        return new UInputMouse(fd);
    }

    public void Write(ushort type, ushort code, int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var buffer = BuildInputEvent(type, code, value);
        if (LibC.write(_fd, buffer, (nuint)buffer.Length) != buffer.Length)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to write uinput event.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LibC.ioctl(_fd, UiDevDestroy, 0);
        LibC.close(_fd);
    }

    /// <summary>struct uinput_user_dev(1116 バイト)。</summary>
    internal static byte[] BuildUserDev(string deviceName)
    {
        var buffer = new byte[UinputMaxNameSize + 8 + 4 + 4 * AbsCnt * 4];
        var name = Encoding.ASCII.GetBytes(deviceName);
        Array.Copy(name, buffer, Math.Min(name.Length, UinputMaxNameSize - 1));

        var offset = UinputMaxNameSize;
        BitConverter.TryWriteBytes(buffer.AsSpan(offset, 2), (ushort)BusUsb);
        BitConverter.TryWriteBytes(buffer.AsSpan(offset + 2, 2), (ushort)0x4241); // vendor
        BitConverter.TryWriteBytes(buffer.AsSpan(offset + 4, 2), (ushort)0x0002); // product
        BitConverter.TryWriteBytes(buffer.AsSpan(offset + 6, 2), (ushort)0x0001); // version
        return buffer;
    }

    /// <summary>struct input_event。タイムスタンプは 0 のままで、カーネルが設定する。</summary>
    internal static byte[] BuildInputEvent(ushort type, ushort code, int value)
    {
        var buffer = new byte[2 * IntPtr.Size + 8];
        var offset = 2 * IntPtr.Size;
        BitConverter.TryWriteBytes(buffer.AsSpan(offset, 2), type);
        BitConverter.TryWriteBytes(buffer.AsSpan(offset + 2, 2), code);
        BitConverter.TryWriteBytes(buffer.AsSpan(offset + 4, 4), value);
        return buffer;
    }

    private static void Ioctl(int fd, uint request, int arg)
    {
        if (LibC.ioctl(fd, request, arg) < 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"uinput ioctl 0x{request:X} failed.");
        }
    }
}
