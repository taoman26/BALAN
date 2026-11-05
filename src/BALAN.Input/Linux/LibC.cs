using System.Runtime.InteropServices;

namespace BALAN.Input.Linux;

/// <summary>libc の open / ioctl / write / close。</summary>
internal static class LibC
{
    public const int ORdWr = 2;
    public const int ONonBlock = 0x800;

    [DllImport("libc", SetLastError = true)]
    public static extern int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int ioctl(int fd, nuint request, int arg);

    [DllImport("libc", SetLastError = true)]
    public static extern nint write(int fd, byte[] buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);
}
