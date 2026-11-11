using BALAN.Core.Domain;

namespace BALAN.Core.Abstractions;

/// <summary>荷重センサー付きデバイス(Wii Balance Board)。BALAN.Device が実装する。</summary>
public interface IBalanceBoard
{
    ConnectionState State { get; }

    /// <summary>電池残量(0〜100)。不明/未接続は 0。</summary>
    int BatteryPercent { get; }

    /// <summary>
    /// ボード自身が「電池残量わずか」と報告している。%表示は電池の特性上、切れる直前まで高いままのことがあるため、
    /// 電池交換の合図にはこちらを使う。変化したときも <see cref="BatteryChanged"/> が発火する。
    /// </summary>
    bool BatteryLow { get; }

    /// <summary>接続を試みる。見つからなければ <see cref="TimeoutException"/>。</summary>
    Task ConnectAsync();

    Task DisconnectAsync();

    /// <summary>荷重を受信したとき(読み取りスレッドから、約 100 Hz)。</summary>
    event EventHandler<SensorFrame> FrameReceived;

    event EventHandler<ConnectionState> ConnectionStateChanged;

    event EventHandler<int> BatteryChanged;
}
