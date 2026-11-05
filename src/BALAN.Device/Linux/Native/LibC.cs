using System.Runtime.InteropServices;

namespace BALAN.Device.Linux.Native;

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

    // --- Bluetooth L2CAP ソケット ---
    public const int AfBluetooth = 31;
    public const int SockSeqPacket = 5;
    public const int BtProtoL2Cap = 0;
    public const int SolSocket = 1;
    public const int SoSndTimeo = 21;
    public const int ShutRdWr = 2;
    public const short PollIn = 0x001;
    public const short PollErr = 0x008;
    public const short PollHup = 0x010;

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TimeVal
    {
        public long tv_sec;
        public long tv_usec;
    }

    [DllImport("libc", SetLastError = true)]
    public static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    public static extern int connect(int fd, byte[] addr, uint addrLen);

    [DllImport("libc", SetLastError = true)]
    public static extern int setsockopt(int fd, int level, int optName, ref TimeVal optVal, uint optLen);

    [DllImport("libc", SetLastError = true)]
    public static extern int shutdown(int fd, int how);

    [DllImport("libc", SetLastError = true)]
    public static extern int poll(ref PollFd fds, nuint nfds, int timeoutMilliseconds);

    [DllImport("libc", SetLastError = true)]
    public static extern nint recv(int fd, byte[] buffer, nuint length, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern nint send(int fd, byte[] buffer, nuint length, int flags);
}
