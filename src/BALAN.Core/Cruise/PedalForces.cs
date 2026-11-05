using BALAN.Core.Domain;
using BALAN.Core.Settings;

namespace BALAN.Core.Cruise;

/// <summary>
/// 足の荷重を、操作として読み替えたもの。
/// TR=上スクロール、BR=下スクロール(大きい方が勝つ)、TL/BL=ブレーキ(大きい方)。
/// </summary>
public readonly record struct PedalForces(double Up, double Down, double Brake)
{
    public static PedalForces From(in SensorFrame f) => new(
        Up: f.TopRight,
        Down: f.BottomRight,
        Brake: Math.Max(f.TopLeft, f.BottomLeft));

    /// <summary>無操作の状態(足を載せただけ)の荷重を差し引く。負にはならない。</summary>
    public static SensorFrame Tare(in SensorFrame f, BalanSettings s) => new(
        Math.Max(0, f.TopLeft - s.TareTopLeft),
        Math.Max(0, f.TopRight - s.TareTopRight),
        Math.Max(0, f.BottomLeft - s.TareBottomLeft),
        Math.Max(0, f.BottomRight - s.TareBottomRight));
}
