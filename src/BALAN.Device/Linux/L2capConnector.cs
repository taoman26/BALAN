using System.ComponentModel;
using System.Text.RegularExpressions;
using BALAN.Device.Transport;
using BALAN.Device.Linux.Native;

namespace BALAN.Device.Linux;

/// <summary>Bluetooth アドレスを指定して Balance Board へ直接 L2CAP 接続する。</summary>
internal interface IL2capConnector
{
    /// <exception cref="IOException">接続できなかった。</exception>
    IBoardConnection Connect(string address);
}

/// <summary>
/// Wii Remote 系(Balance Board を含む)は HID の L2CAP チャネルへの接続時にペアリング(PIN 認証)を必須としないため、
/// BlueZ のボンディングを経由せずソケットで直接接続する。これなら PIN の問題が起きない。
/// </summary>
internal sealed partial class L2capConnector : IL2capConnector
{
    private const ushort ControlPsm = 0x11;
    private const ushort InterruptPsm = 0x13;
    private const int ConnectTimeoutSeconds = 5;

    public IBoardConnection Connect(string address)
    {
        var bytes = ParseAddress(address);
        var control = OpenChannel(bytes, ControlPsm);
        try
        {
            var interrupt = OpenChannel(bytes, InterruptPsm);
            return new L2capBoardConnection(control, interrupt);
        }
        catch
        {
            LibC.close(control);
            throw;
        }
    }

    /// <summary>"AA:BB:CC:DD:EE:FF" を bdaddr_t の並び(下位バイトから)にする。</summary>
    internal static byte[] ParseAddress(string address)
    {
        if (!AddressPattern().IsMatch(address))
        {
            throw new ArgumentException($"Invalid Bluetooth address: {address}", nameof(address));
        }

        return address.Split(':').Select(p => Convert.ToByte(p, 16)).Reverse().ToArray();
    }

    /// <summary>struct sockaddr_l2(14 バイト)を組み立てる。</summary>
    internal static byte[] BuildSockAddr(byte[] bdaddr, ushort psm)
    {
        var addr = new byte[14];
        addr[0] = (byte)LibC.AfBluetooth;           // l2_family(リトルエンディアン)
        addr[2] = (byte)(psm & 0xFF);               // l2_psm
        addr[3] = (byte)(psm >> 8);
        Array.Copy(bdaddr, 0, addr, 4, 6);          // l2_bdaddr
        return addr;                                // l2_cid = 0, l2_bdaddr_type = BDADDR_BREDR(0)
    }

    private static int OpenChannel(byte[] bdaddr, ushort psm)
    {
        var fd = LibC.socket(LibC.AfBluetooth, LibC.SockSeqPacket, LibC.BtProtoL2Cap);
        if (fd < 0)
        {
            throw new IOException(
                $"Cannot create a Bluetooth socket: {new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()).Message}");
        }

        try
        {
            var timeout = new LibC.TimeVal { tv_sec = ConnectTimeoutSeconds };
            LibC.setsockopt(fd, LibC.SolSocket, LibC.SoSndTimeo, ref timeout, (uint)System.Runtime.InteropServices.Marshal.SizeOf<LibC.TimeVal>());

            var addr = BuildSockAddr(bdaddr, psm);
            if (LibC.connect(fd, addr, (uint)addr.Length) < 0)
            {
                var error = new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()).Message;
                throw new IOException($"L2CAP connect to PSM 0x{psm:X2} failed: {error}");
            }

            return fd;
        }
        catch
        {
            LibC.close(fd);
            throw;
        }
    }

    [GeneratedRegex("^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$")]
    private static partial Regex AddressPattern();
}
