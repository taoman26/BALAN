namespace BALAN.Core.Settings;

/// <summary>スクロールの方式。</summary>
public enum ScrollMode
{
    /// <summary>垂直の巡航スクロール。右足 = 加速(TR 上 / BR 下)、左足 = ブレーキ。</summary>
    Vertical,

    /// <summary>水平(チルトホイール)スクロール。右足(TR / BR)= 右、左足(TL / BL)= 左。ブレーキは使わない。</summary>
    Horizontal
}

/// <summary>ユーザー設定(JSON で保存)。荷重はすべて kg(ボード内蔵の較正済みの値)。</summary>
public sealed record BalanSettings
{
    /// <summary>
    /// この荷重(kg)未満は無反応。ここからが Slow、+1 kg で Medium、+2 kg で Fast の目安になる。
    /// </summary>
    public double DeadZoneKg { get; init; } = 2.0;

    /// <summary>この荷重(kg)で最大速度になる。</summary>
    public double FullLoadKg { get; init; } = 5.0;

    /// <summary>左側センサー(TL / BL)のどちらかがこの荷重(kg)以上でブレーキが効く。</summary>
    public double BrakeLoadKg { get; init; } = 5.0;

    /// <summary>最大荷重のときの速度(ノッチ/秒)。</summary>
    public double MaxSpeed { get; init; } = 15;

    /// <summary>ブレーキの効きの強さ 0.0〜1.0(大きいほど速く止まる)。</summary>
    public double BrakeStrength { get; init; } = 0.6;

    /// <summary>スクロールの方式(既定は垂直)。</summary>
    public ScrollMode ScrollMode { get; init; } = ScrollMode.Vertical;

    /// <summary>スクロール送出の ON/OFF(OFF なら画面表示のみ)。</summary>
    public bool ScrollEnabled { get; init; } = true;

    /// <summary>キャリブレーションで測った、足を載せただけの状態(無操作)の各センサー荷重。これを差し引く。</summary>
    public double TareTopLeft { get; init; }
    public double TareTopRight { get; init; }
    public double TareBottomLeft { get; init; }
    public double TareBottomRight { get; init; }

    /// <summary>スクロールを送る最小単位(120 = 1 ノッチ)。小さいほど滑らかだが、細かい値を無視するアプリがある。</summary>
    public int WheelGranularity { get; init; } = 30;

    /// <summary>起動時にウィンドウを出さずトレイへ。</summary>
    public bool StartMinimized { get; init; }

    /// <summary>範囲外の値を安全な範囲へ丸める(手編集された JSON 対策)。</summary>
    public BalanSettings Normalize()
    {
        var dead = Math.Clamp(Sanitize(DeadZoneKg, 2.0), 0.3, 6);
        return this with
        {
            DeadZoneKg = dead,
            FullLoadKg = Math.Clamp(Sanitize(FullLoadKg, 5.0), dead + 0.5, 40),
            BrakeLoadKg = Math.Clamp(Sanitize(BrakeLoadKg, 5.0), 0.5, 40),
            MaxSpeed = Math.Clamp(Sanitize(MaxSpeed, 15), 1, 60),
            BrakeStrength = Math.Clamp(Sanitize(BrakeStrength, 0.6), 0, 1),
            TareTopLeft = Math.Clamp(Sanitize(TareTopLeft, 0), 0, 10),
            TareTopRight = Math.Clamp(Sanitize(TareTopRight, 0), 0, 10),
            TareBottomLeft = Math.Clamp(Sanitize(TareBottomLeft, 0), 0, 10),
            TareBottomRight = Math.Clamp(Sanitize(TareBottomRight, 0), 0, 10),
            ScrollMode = Enum.IsDefined(ScrollMode) ? ScrollMode : ScrollMode.Vertical,
            WheelGranularity = Math.Clamp(WheelGranularity, 1, 120),
        };
    }

    private static double Sanitize(double value, double fallback) => double.IsFinite(value) ? value : fallback;
}
