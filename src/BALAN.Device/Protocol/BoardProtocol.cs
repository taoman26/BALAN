namespace BALAN.Device.Protocol;

/// <summary>
/// Wii Balance Board(RVL-WBC-01)の HID プロトコル。Wii Remote と同じ HID レポート体系で、荷重は拡張コントローラ扱い。
/// 参照: WiiBrew "Wii Balance Board" / "Wiimote" ページ。
/// </summary>
public static class BoardProtocol
{
    public const int NintendoVendorId = 0x057E;

    /// <summary>Balance Board の Product ID は Wii Remote(初代)と同じ 0x0306。拡張 ID で区別する。</summary>
    public static readonly IReadOnlyList<int> ProductIds = new[] { 0x0306, 0x0330 };

    // 入力レポート ID
    public const byte StatusReportId = 0x20;
    public const byte ReadDataReportId = 0x21;
    public const byte AckReportId = 0x22;

    /// <summary>ボタン(2 バイト)+ 拡張 8 バイト。Balance Board の荷重はこのレポートで届く。</summary>
    public const byte ExtensionReportId = 0x32;

    // 出力レポート ID
    private const byte OutputLedId = 0x11;
    private const byte OutputReportModeId = 0x12;
    private const byte OutputStatusRequestId = 0x15;
    private const byte OutputWriteMemoryId = 0x16;
    private const byte OutputReadMemoryId = 0x17;

    /// <summary>メモリではなくレジスタ空間を指す(アドレス空間フラグ)。</summary>
    private const byte RegisterSpace = 0x04;

    // 拡張コントローラのレジスタ
    public const int ExtensionInitAddress1 = 0xA400F0;
    public const int ExtensionInitAddress2 = 0xA400FB;
    public const int ExtensionIdAddress = 0xA400FA;
    public const int CalibrationAddress = 0xA40024;
    public const int CalibrationLength = 24;

    /// <summary>Balance Board の拡張 ID(0xA400FA から 6 バイト)。</summary>
    public static readonly byte[] BalanceBoardExtensionId = { 0x00, 0x00, 0xA4, 0x20, 0x04, 0x02 };

    public static byte[] StatusRequest() => new byte[] { OutputStatusRequestId, 0x00 };

    /// <summary>LED を点灯する。<paramref name="led"/> は 1〜4(ボードには 1 つだけ)。</summary>
    public static byte[] SetLed(int led)
    {
        if (led is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(led), "LED must be 1 to 4.");
        }

        return new byte[] { OutputLedId, (byte)(0x10 << (led - 1)) };
    }

    /// <summary>連続レポート + ボタン/拡張 8 バイト(0x32)。変化がなくても約 100 Hz で届く。</summary>
    public static byte[] SetReportModeExtension8() => new byte[] { OutputReportModeId, 0x04, ExtensionReportId };

    /// <summary>レジスタへ 1 バイト書く。</summary>
    public static byte[] WriteRegister(int address, byte value)
    {
        var report = new byte[22];
        report[0] = OutputWriteMemoryId;
        report[1] = RegisterSpace;
        report[2] = (byte)(address >> 16);
        report[3] = (byte)(address >> 8);
        report[4] = (byte)address;
        report[5] = 1;
        report[6] = value;
        return report;
    }

    /// <summary>レジスタを読む要求。応答は 0x21 レポート(1 回 16 バイトまで、複数に分かれる)。</summary>
    public static byte[] ReadRegister(int address, int length)
    {
        if (length is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return new byte[]
        {
            OutputReadMemoryId, RegisterSpace,
            (byte)(address >> 16), (byte)(address >> 8), (byte)address,
            (byte)(length >> 8), (byte)length
        };
    }
}
