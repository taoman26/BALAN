using System.Runtime.InteropServices;
using BALAN.Core.Abstractions;

namespace BALAN.Input;

/// <summary>Windows の SendInput でマウスホイールを送る。カーソル位置のウィンドウがスクロールされる。</summary>
public sealed class WindowsScrollSender : IScrollSender
{
    private const uint InputMouse = 0;
    private const uint MouseEventFWheel = 0x0800;
    private const uint MouseEventFHWheel = 0x1000; // 水平ホイール(正 = 右)

    public void SendVertical(int wheelDelta) => SendWheel(wheelDelta, MouseEventFWheel);

    public void SendHorizontal(int wheelDelta) => SendWheel(wheelDelta, MouseEventFHWheel);

    private static void SendWheel(int wheelDelta, uint flag)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        var input = new INPUT
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    // mouseData は符号付き(垂直: 正 = 奥へ = 上、水平: 正 = 右)を DWORD に詰める。
                    mouseData = unchecked((uint)wheelDelta),
                    dwFlags = flag,
                }
            }
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) != 1)
        {
            throw new InvalidOperationException($"SendInput failed (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // INPUT のサイズ(x64 で 40 バイト)は最大メンバーの MOUSEINPUT で決まる。
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
