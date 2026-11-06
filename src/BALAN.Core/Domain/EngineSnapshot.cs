namespace BALAN.Core.Domain;

/// <summary>スクロール方向。</summary>
public enum ScrollDirection
{
    None,
    Up,
    Down,
    Left,
    Right
}

/// <summary>UI 表示用の、ある瞬間のエンジンの状態。</summary>
/// <param name="Frame">平滑化・無操作分の差し引き後の各センサー荷重(kg)。センサー未受信なら null。</param>
/// <param name="HasData">センサー入力が届いている(途絶えていない)。</param>
/// <param name="ForceUp">正方向のアクセル荷重。垂直: TR(上)/ 水平: TR・BR の大きい方(右)。</param>
/// <param name="ForceDown">負方向のアクセル荷重。垂直: BR(下)/ 水平: TL・BL の大きい方(左)。</param>
/// <param name="ForceBrake">垂直: TL / BL のうち大きい方 = ブレーキ / 水平: 常に 0。</param>
/// <param name="Speed">現在の巡航速度(ノッチ/秒。1 ノッチ=マウスホイール 1 目盛り)。</param>
/// <param name="Direction">スクロール中の方向(停止中は None)。</param>
/// <param name="Level">速度の段階。</param>
/// <param name="IsBraking">ブレーキが効いている。</param>
public sealed record EngineSnapshot(
    SensorFrame? Frame,
    bool HasData,
    double ForceUp,
    double ForceDown,
    double ForceBrake,
    double Speed,
    ScrollDirection Direction,
    CruiseLevel Level,
    bool IsBraking)
{
    public static EngineSnapshot Idle { get; } = new(null, false, 0, 0, 0, 0, ScrollDirection.None, CruiseLevel.Stopped, false);
}
