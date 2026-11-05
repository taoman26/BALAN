namespace BALAN.Core.Domain;

/// <summary>スクロール方向。</summary>
public enum ScrollDirection
{
    None,
    Up,
    Down
}

/// <summary>UI 表示用の、ある瞬間のエンジンの状態。</summary>
/// <param name="Frame">平滑化・無操作分の差し引き後の各センサー荷重(kg)。センサー未受信なら null。</param>
/// <param name="HasData">センサー入力が届いている(途絶えていない)。</param>
/// <param name="ForceUp">TR の荷重 = 上スクロールのアクセル。</param>
/// <param name="ForceDown">BR の荷重 = 下スクロールのアクセル。</param>
/// <param name="ForceBrake">TL / BL のうち大きい方 = ブレーキ。</param>
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
