using System.Runtime.InteropServices;
using BALAN.Device.Windows.Native;

namespace BALAN.Device.Windows.Bluetooth;

/// <summary><see cref="IBluetoothApi"/> の bthprops.cpl による実装。開いたラジオのハンドルは Dispose で閉じる。</summary>
internal sealed class Win32BluetoothApi : IBluetoothApi
{
    private readonly List<nint> _handles = new();
    private bool _disposed;

    public IReadOnlyList<BluetoothRadio> GetRadios()
    {
        var radios = new List<BluetoothRadio>();
        var findParams = new BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS>(),
        };

        var find = BluetoothNative.BluetoothFindFirstRadio(ref findParams, out var radioHandle);
        if (find == IntPtr.Zero)
        {
            return radios; // Bluetooth アダプターが無い(または無効)。
        }

        try
        {
            do
            {
                _handles.Add(radioHandle);
                var info = new BluetoothNative.BLUETOOTH_RADIO_INFO
                {
                    dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_RADIO_INFO>(),
                    szName = string.Empty,
                };

                if (BluetoothNative.BluetoothGetRadioInfo(radioHandle, ref info) == BluetoothNative.ErrorSuccess)
                {
                    radios.Add(new BluetoothRadio(radioHandle, info.address, info.szName ?? string.Empty));
                }
            }
            while (BluetoothNative.BluetoothFindNextRadio(find, out radioHandle));
        }
        finally
        {
            BluetoothNative.BluetoothFindRadioClose(find);
        }

        return radios;
    }

    public IReadOnlyList<BluetoothDevice> Discover(BluetoothRadio radio, int timeoutMultiplier)
    {
        var devices = new List<BluetoothDevice>();
        var search = new BluetoothNative.BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = 1,
            fReturnRemembered = 1,
            fReturnUnknown = 1,
            fReturnConnected = 1,
            fIssueInquiry = 1,
            cTimeoutMultiplier = (byte)Math.Clamp(timeoutMultiplier, 1, 48),
            hRadio = radio.Handle,
        };

        var info = NewDeviceInfo(0, string.Empty);
        var find = BluetoothNative.BluetoothFindFirstDevice(ref search, ref info);
        if (find == IntPtr.Zero)
        {
            return devices; // 見つからない場合もここ(エラーではない)。
        }

        try
        {
            do
            {
                devices.Add(new BluetoothDevice(
                    info.Address,
                    info.szName ?? string.Empty,
                    info.fConnected != 0,
                    info.fRemembered != 0,
                    info.fAuthenticated != 0));
            }
            while (BluetoothNative.BluetoothFindNextDevice(find, ref info));
        }
        finally
        {
            BluetoothNative.BluetoothFindDeviceClose(find);
        }

        return devices;
    }

    public uint Authenticate(BluetoothRadio radio, BluetoothDevice device, byte[] pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var info = NewDeviceInfo(device.Address, device.Name);

        // PIN の各バイトを 1 文字(下位バイト)として渡す。
        var passkey = new string(pin.Select(b => (char)b).ToArray());
        return BluetoothNative.BluetoothAuthenticateDevice(
            IntPtr.Zero, radio.Handle, ref info, passkey, (uint)pin.Length);
    }

    public uint EnableHidService(BluetoothRadio radio, BluetoothDevice device)
    {
        var info = NewDeviceInfo(device.Address, device.Name);
        var service = BluetoothNative.HidServiceClass;
        return BluetoothNative.BluetoothSetServiceState(
            radio.Handle, ref info, ref service, BluetoothNative.BluetoothServiceEnable);
    }

    public uint Remove(BluetoothDevice device)
    {
        var address = device.Address;
        return BluetoothNative.BluetoothRemoveDevice(ref address);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var handle in _handles)
        {
            BluetoothNative.CloseHandle(handle);
        }

        _handles.Clear();
    }

    private static BluetoothNative.BLUETOOTH_DEVICE_INFO NewDeviceInfo(ulong address, string name) => new()
    {
        dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_DEVICE_INFO>(),
        Address = address,
        szName = name,
    };
}
