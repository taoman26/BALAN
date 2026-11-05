namespace BALAN.Device.Transport;

/// <summary>開いている HID デバイスとの接続。</summary>
public interface IBoardConnection : IDisposable
{
    int InputReportLength { get; }

    /// <summary>1 レポート読む。一定時間来なければ 0。切断されたら <see cref="IOException"/>。</summary>
    int Read(byte[] buffer);

    /// <summary>出力レポート(先頭がレポート ID)を送る。</summary>
    void Write(byte[] report);
}

/// <summary>Wii 系 HID デバイスの検出。HID ライブラリへの依存をここに閉じ込める。</summary>
public interface IBoardTransport
{
    /// <summary>
    /// 候補(Nintendo の Wii Remote 系 HID)を 1 つずつ開いて返す。Balance Board かどうかは呼び出し側が判定する。
    /// 呼び出し側が不適合なら Dispose して次を取りに来る。
    /// </summary>
    IEnumerable<IBoardConnection> OpenCandidates();
}
