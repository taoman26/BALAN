using BALAN.Core.Domain;

namespace BALAN.Device.Protocol;

/// <summary>入力レポートの解析。</summary>
public static class BoardReportParser
{
    /// <summary>0x32 レポート: [ID][ボタン 2][拡張 8] = 11 バイト以上。</summary>
    public static bool TryParseRawSensors(ReadOnlySpan<byte> report, out RawSensors raw)
    {
        raw = default;
        if (report.Length < 11 || report[0] != BoardProtocol.ExtensionReportId)
        {
            return false;
        }

        // 並びは TR, BR, TL, BL。各 16 ビット ビッグエンディアン。
        raw = new RawSensors(
            TopRight: ReadU16(report, 3),
            BottomRight: ReadU16(report, 5),
            TopLeft: ReadU16(report, 7),
            BottomLeft: ReadU16(report, 9));
        return true;
    }

    /// <summary>
    /// ステータスレポート(0x20): [ID][ボタン 2][フラグ][0][0][電池]。
    /// フラグのビット 1 = 拡張コントローラ接続。
    /// </summary>
    public static bool TryParseStatus(ReadOnlySpan<byte> report, out bool extensionConnected, out int batteryPercent)
    {
        extensionConnected = false;
        batteryPercent = 0;
        if (report.Length < 7 || report[0] != BoardProtocol.StatusReportId)
        {
            return false;
        }

        extensionConnected = (report[3] & 0x02) != 0;
        // Wii Remote と同様、0〜200 を 0〜100% とみなす。Balance Board 実機での最大値は要確認(docs/DESIGN.md のリスク参照)。
        batteryPercent = Math.Clamp((int)Math.Round(report[6] * 100.0 / 200), 0, 100);
        return true;
    }

    /// <summary>
    /// メモリ読み出し応答(0x21): [ID][ボタン 2][サイズ-1 (上位 4bit) | エラー (下位 4bit)][アドレス 2][データ 最大 16]。
    /// </summary>
    public static bool TryParseReadData(ReadOnlySpan<byte> report, out int address, out int error, out ReadOnlySpan<byte> data)
    {
        address = 0;
        error = 0;
        data = default;
        if (report.Length < 6 || report[0] != BoardProtocol.ReadDataReportId)
        {
            return false;
        }

        var size = (report[3] >> 4) + 1;
        error = report[3] & 0x0F;
        address = (report[4] << 8) | report[5];
        if (report.Length < 6 + size)
        {
            return false;
        }

        data = report.Slice(6, size);
        return true;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> b, int index) => (ushort)((b[index] << 8) | b[index + 1]);
}

/// <summary>センサーの生値(未較正)。</summary>
public readonly record struct RawSensors(ushort TopLeft, ushort TopRight, ushort BottomLeft, ushort BottomRight);
