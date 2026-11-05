namespace BALAN.Device.Transport;

public enum PairingResult
{
    /// <summary>この OS ではアプリ側でペアリングしない(OS の Bluetooth 設定に任せる)。</summary>
    NotSupported,

    /// <summary>探索したが Balance Board が見つからなかった(SYNC ボタンが押されていない等)。</summary>
    NoDeviceFound,

    /// <summary>ペアリング済み、または今回成功し、HID として使える状態にした。</summary>
    Paired,

    /// <summary>見つかったがペアリングに失敗した。</summary>
    Failed
}

/// <summary>
/// Balance Board を OS の Bluetooth スタックへ自動でペアリングする。
/// Windows の標準 UI は PIN を求めるが、その値は Bluetooth アドレスのバイナリで手入力できないため、アプリが代行する。
/// </summary>
public interface IBoardPairing
{
    /// <summary>1 回分の探索とペアリング(数秒かかる)。失敗は例外ではなく戻り値で返す。</summary>
    Task<PairingResult> TryPairAsync(CancellationToken cancellationToken);
}
