namespace BALAN.Device.Windows.Bluetooth;

/// <summary>Bluetooth アダプター(ラジオ)。<see cref="Handle"/> は実装が解釈する不透明な値。</summary>
internal sealed record BluetoothRadio(nint Handle, ulong Address, string Name);

/// <summary>探索で見つかった Bluetooth デバイス。<see cref="Address"/> は MAC アドレスを整数にした値。</summary>
internal sealed record BluetoothDevice(
    ulong Address,
    string Name,
    bool Connected,
    bool Remembered,
    bool Authenticated);

/// <summary>Windows Bluetooth API の薄いラッパー。ペアリング手順を OS 非依存でテストするための境界。</summary>
internal interface IBluetoothApi : IDisposable
{
    IReadOnlyList<BluetoothRadio> GetRadios();

    /// <summary>探索(インクワイアリ)を行い、見えているデバイスを返す。<paramref name="timeoutMultiplier"/> × 1.28 秒かかる。</summary>
    IReadOnlyList<BluetoothDevice> Discover(BluetoothRadio radio, int timeoutMultiplier);

    /// <summary>PIN を渡してペアリングする。戻り値は Win32 エラーコード(0 = 成功)。</summary>
    uint Authenticate(BluetoothRadio radio, BluetoothDevice device, byte[] pin);

    /// <summary>HID サービスを有効にして、OS に HID デバイスとして認識させる。戻り値は Win32 エラーコード。</summary>
    uint EnableHidService(BluetoothRadio radio, BluetoothDevice device);

    /// <summary>OS に残っているペアリング情報を削除する。戻り値は Win32 エラーコード。</summary>
    uint Remove(BluetoothDevice device);
}
