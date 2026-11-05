using System.Runtime.CompilerServices;
using BALAN.Core.Abstractions;
using BALAN.Device.Transport;
using BALAN.Device.Windows.Bluetooth;

[assembly: InternalsVisibleTo("BALAN.Tests")]

namespace BALAN.Device.Windows;

/// <summary>
/// Windows で Balance Board を自動ペアリングする(KUNAI の Wii Remote 用実装を Balance Board 向けに移植)。
/// Balance Board は赤い SYNC ボタン(電池ボックス内)でのみペアリングでき、その場合の PIN は PC 側アダプターのアドレス
/// (下位バイトから 6 バイト)。念のためボード自身のアドレスも試す。
/// </summary>
public sealed class WindowsBoardPairing : IBoardPairing
{
    /// <summary>探索 1 回の長さ(× 1.28 秒)。</summary>
    private const int DiscoveryTimeoutMultiplier = 3;

    private readonly IAppLog _log;
    private readonly Func<IBluetoothApi> _apiFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ulong _lastReportedAddress;
    private bool _noRadioReported;

    public WindowsBoardPairing(IAppLog log)
        : this(log, () => new Win32BluetoothApi())
    {
    }

    internal WindowsBoardPairing(IAppLog log, Func<IBluetoothApi> apiFactory)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
    }

    public async Task<PairingResult> TryPairAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => TryPair(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>PIN: アドレス整数の下位バイトから 6 バイト。</summary>
    internal static byte[] PinFromAddress(ulong address)
    {
        var bytes = new byte[6];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((address >> (8 * i)) & 0xFF);
        }

        return bytes;
    }

    /// <summary>Balance Board の Bluetooth デバイス名は "Nintendo RVL-WBC-01"。</summary>
    internal static bool IsBoard(BluetoothDevice device) =>
        device.Name.StartsWith("Nintendo RVL-WBC-01", StringComparison.OrdinalIgnoreCase);

    private PairingResult TryPair(CancellationToken token)
    {
        try
        {
            using var api = _apiFactory();
            var radios = api.GetRadios();
            if (radios.Count == 0)
            {
                if (!_noRadioReported)
                {
                    _noRadioReported = true;
                    _log.Info("Bluetooth adapter not found (is Bluetooth turned on?)");
                }

                return PairingResult.NoDeviceFound;
            }

            var sawBoard = false;
            foreach (var radio in radios)
            {
                token.ThrowIfCancellationRequested();
                foreach (var device in api.Discover(radio, DiscoveryTimeoutMultiplier).Where(IsBoard))
                {
                    sawBoard = true;
                    token.ThrowIfCancellationRequested();
                    if (PairDevice(api, radio, device))
                    {
                        return PairingResult.Paired;
                    }
                }
            }

            return sawBoard ? PairingResult.Failed : PairingResult.NoDeviceFound;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _log.Info($"Bluetooth API unavailable: {ex.Message}");
            return PairingResult.NotSupported;
        }
        catch (Exception ex)
        {
            _log.Info($"Pairing error: {ex.Message}");
            return PairingResult.Failed;
        }
    }

    private bool PairDevice(IBluetoothApi api, BluetoothRadio radio, BluetoothDevice device)
    {
        if (_lastReportedAddress != device.Address)
        {
            _lastReportedAddress = device.Address;
            _log.Info($"Balance Board found ({device.Name}), pairing...");
        }

        // すでに接続済みなら認証は不要。HID サービスだけ確実に有効にする。
        if (device.Connected && device.Authenticated)
        {
            api.EnableHidService(radio, device);
            return true;
        }

        // 古いペアリング情報が残っていると新しい認証が失敗するため消す。
        if (device.Remembered || device.Authenticated)
        {
            api.Remove(device);
        }

        var pins = new[] { PinFromAddress(radio.Address), PinFromAddress(device.Address) };
        uint lastError = 0;
        foreach (var pin in pins)
        {
            lastError = api.Authenticate(radio, device, pin);
            if (lastError == 0)
            {
                var hidError = api.EnableHidService(radio, device);
                if (hidError != 0)
                {
                    _log.Info($"Paired, but enabling the HID service returned error {hidError}");
                }

                _log.Info("Balance Board paired");
                return true;
            }
        }

        _log.Info($"Pairing failed (error {lastError}). Press the red SYNC button again.");
        return false;
    }
}
