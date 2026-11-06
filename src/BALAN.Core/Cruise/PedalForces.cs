using BALAN.Core.Domain;
using BALAN.Core.Settings;

namespace BALAN.Core.Cruise;

/// <summary>
/// 足の荷重を、操作として読み替えたもの。
/// 垂直: TR=上スクロール、BR=下スクロール(大きい方が勝つ)、TL/BL=ブレーキ(大きい方)。
/// 水平: 右足(TR/BR の大きい方)=右、左足(TL/BL の大きい方)=左、ブレーキなし。
/// Up / Down は「正方向 / 負方向」のアクセルで、速度の計算(<see cref="CruiseController"/>)は垂直・水平で共通。
/// </summary>
public readonly record struct PedalForces(double Up, double Down, double Brake)
{
    public static PedalForces From(in SensorFrame f) => new(
        Up: f.TopRight,
        Down: f.BottomRight,
        Brake: Math.Max(f.TopLeft, f.BottomLeft));

    /// <summary>水平モード。Up = 右(正)、Down = 左(負)。Brake は常に 0(ブレーキ概念を使わない)。</summary>
    public static PedalForces FromHorizontal(in SensorFrame f) => new(
        Up: Math.Max(f.TopRight, f.BottomRight),
        Down: Math.Max(f.TopLeft, f.BottomLeft),
        Brake: 0);

    public static PedalForces From(in SensorFrame f, ScrollMode mode) =>
        mode == ScrollMode.Horizontal ? FromHorizontal(f) : From(f);

    /// <summary>無操作の状態(足を載せただけ)の荷重を差し引く。負にはならない。</summary>
    public static SensorFrame Tare(in SensorFrame f, BalanSettings s) => new(
        Math.Max(0, f.TopLeft - s.TareTopLeft),
        Math.Max(0, f.TopRight - s.TareTopRight),
        Math.Max(0, f.BottomLeft - s.TareBottomLeft),
        Math.Max(0, f.BottomRight - s.TareBottomRight));
}
