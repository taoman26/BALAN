using BALAN.Core.Abstractions;

namespace BALAN.Input.Linux;

/// <summary>
/// uinput の仮想マウスでホイールを送る(X11 / Wayland のどちらでもカーネル層で動作する)。
/// <para>
/// 高分解能ホイール(REL_WHEEL_HI_RES、120 = 1 ノッチ)を毎回送り、120 たまるごとに従来の REL_WHEEL(1 ノッチ)も送る。
/// カーネルの高分解能ホイールの作法どおりで、対応アプリは滑らかに、非対応アプリは 1 ノッチ単位で動く。
/// </para>
/// 仮想デバイスは最初の送出時に作成し、破棄時に削除する。
/// </summary>
public sealed class LinuxScrollSender : IScrollSender, IDisposable
{
    private const string DeviceName = "BALAN Virtual Mouse";

    private readonly Func<IInputEventSink> _deviceFactory;
    private readonly object _gate = new();
    private IInputEventSink? _device;
    private int _lowResRemainder;
    private int _hLowResRemainder;
    private bool _disposed;

    public LinuxScrollSender()
        : this(CreateDevice)
    {
    }

    internal LinuxScrollSender(Func<IInputEventSink> deviceFactory)
    {
        _deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
    }

    /// <exception cref="IOException">/dev/uinput を開けない(権限・モジュール未ロード)。設定手順がメッセージに含まれる。</exception>
    public void SendVertical(int wheelDelta) =>
        SendWheel(wheelDelta, UInputMouse.RelWheelHiRes, UInputMouse.RelWheel, ref _lowResRemainder);

    /// <summary>水平スクロール(REL_HWHEEL / REL_HWHEEL_HI_RES)。正 = 右。</summary>
    public void SendHorizontal(int wheelDelta) =>
        SendWheel(wheelDelta, UInputMouse.RelHWheelHiRes, UInputMouse.RelHWheel, ref _hLowResRemainder);

    private void SendWheel(int wheelDelta, ushort hiResCode, ushort lowResCode, ref int lowResRemainder)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _device ??= _deviceFactory();

            _device.Write(UInputMouse.EvRel, hiResCode, wheelDelta);

            lowResRemainder += wheelDelta;
            var notches = lowResRemainder / 120;
            if (notches != 0)
            {
                lowResRemainder -= notches * 120;
                _device.Write(UInputMouse.EvRel, lowResCode, notches);
            }

            _device.Write(UInputMouse.EvSyn, 0, 0); // SYN_REPORT
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _device?.Dispose();
            _device = null;
        }
    }

    private static IInputEventSink CreateDevice()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("LinuxScrollSender is only supported on Linux.");
        }

        return UInputMouse.Create(DeviceName);
    }
}
