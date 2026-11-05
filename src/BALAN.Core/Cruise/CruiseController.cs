using BALAN.Core.Domain;
using BALAN.Core.Settings;

namespace BALAN.Core.Cruise;

/// <summary>
/// 巡航制御。足の荷重から、巡航スクロールの速度と方向を決める。位置ではなく速度を制御する。
/// <list type="bullet">
/// <item>アクセル: TR(上)と BR(下)のうち大きい方。<see cref="BalanSettings.DeadZoneKg"/> 未満は無反応。</item>
/// <item>速度: depth = (荷重 - DeadZone) / (FullLoad - DeadZone) を 0〜1 に丸め、速度 = depth² × MaxSpeed。階段ではなく連続的なカーブ。</item>
/// <item>ブレーキ: TL / BL のどちらかが <see cref="BalanSettings.BrakeLoadKg"/> 以上。アクセルより常に優先し、素早く停止する。</item>
/// <item>方向の切替: 一度 0 まで減速してから反転する(急な逆走をしない)。</item>
/// </list>
/// 加速は緩やか、減速は速い非対称。「止めたいときに止まる」を最優先し、誤作動より無反応側に倒す。
/// 速度の増減は MaxSpeed に対する割合/秒で表すため、MaxSpeed を変えても操作感は変わらない。
/// </summary>
public sealed class CruiseController
{
    /// <summary>加速: 最大速度に達するまで約 0.5 秒。</summary>
    public const double AccelRate = 2.0;

    /// <summary>荷重を抜いたときの減速(惰性): 最大速度から約 0.25 秒で停止。</summary>
    public const double CoastRate = 4.0;

    /// <summary>ブレーキ強度 1.0 のとき、惰性に追加される減速。</summary>
    public const double BrakeRateAtMax = 20.0;

    /// <summary>これ未満の速度(MaxSpeed 比)は 0 とみなす。</summary>
    private const double StopEpsilon = 0.01;

    private double _fraction; // 0..1(MaxSpeed 比)
    private ScrollDirection _direction = ScrollDirection.None;

    public double Speed { get; private set; }

    public ScrollDirection Direction { get; private set; }

    public CruiseLevel Level { get; private set; } = CruiseLevel.Stopped;

    public bool IsBraking { get; private set; }

    /// <summary>アクセル曲線。depth 0〜1 → 速度割合 0〜1。</summary>
    public static double Curve(double depth) => depth * depth;

    /// <summary>荷重から踏み込み量 0〜1。DeadZone 以下は 0。</summary>
    public static double Depth(double forceKg, BalanSettings s) =>
        forceKg <= s.DeadZoneKg ? 0 : Math.Clamp((forceKg - s.DeadZoneKg) / (s.FullLoadKg - s.DeadZoneKg), 0, 1);

    /// <summary>荷重から段階を決める。DeadZone 未満 Stopped、+1 kg 未満 Slow、+2 kg 未満 Medium、以降 Fast。</summary>
    public static CruiseLevel LevelFor(double forceKg, BalanSettings s)
    {
        if (forceKg < s.DeadZoneKg)
        {
            return CruiseLevel.Stopped;
        }

        return forceKg < s.DeadZoneKg + 1 ? CruiseLevel.Slow
            : forceKg < s.DeadZoneKg + 2 ? CruiseLevel.Medium
            : CruiseLevel.Fast;
    }

    /// <summary>1 周期進める。</summary>
    /// <param name="hasData">センサー入力が届いているか。false なら即停止する。</param>
    /// <param name="dt">前回からの経過秒。</param>
    public void Update(in PedalForces forces, bool hasData, double dt, BalanSettings settings)
    {
        dt = Math.Clamp(dt, 0, 0.1); // 長い空白(スリープ復帰など)で一気に動かない

        if (!hasData || !double.IsFinite(forces.Up) || !double.IsFinite(forces.Down) || !double.IsFinite(forces.Brake))
        {
            Stop();
            return;
        }

        var braking = forces.Brake >= settings.BrakeLoadKg;

        var wanted = forces.Up > forces.Down ? ScrollDirection.Up : ScrollDirection.Down;
        var force = Math.Max(forces.Up, forces.Down);
        var target = braking ? 0 : Curve(Depth(force, settings));
        if (target <= 0)
        {
            wanted = ScrollDirection.None;
        }

        // 逆方向へ切り替えるときは、いったん 0 まで減速してから。
        if (wanted != ScrollDirection.None && _direction != ScrollDirection.None && wanted != _direction && _fraction > 0)
        {
            target = 0;
        }
        else if (wanted != ScrollDirection.None && _fraction == 0)
        {
            _direction = wanted;
        }

        if (target > _fraction)
        {
            _fraction = Math.Min(target, _fraction + AccelRate * dt);
        }
        else
        {
            var rate = CoastRate + (braking ? BrakeRateAtMax * settings.BrakeStrength : 0);
            _fraction = Math.Max(target, _fraction - rate * dt);
        }

        if (_fraction < StopEpsilon)
        {
            _fraction = 0;
            _direction = ScrollDirection.None;
        }

        IsBraking = braking;
        Publish(settings);
    }

    public void Reset() => Stop();

    private void Stop()
    {
        _fraction = 0;
        _direction = ScrollDirection.None;
        IsBraking = false;
        Speed = 0;
        Direction = ScrollDirection.None;
        Level = CruiseLevel.Stopped;
    }

    private void Publish(BalanSettings s)
    {
        Speed = _fraction * s.MaxSpeed;
        Direction = _fraction == 0 ? ScrollDirection.None : _direction;
        // 速度 = depth² なので、現在の「実効的な荷重」は DeadZone + √速度 × (FullLoad - DeadZone)。
        Level = _fraction == 0
            ? CruiseLevel.Stopped
            : LevelFor(s.DeadZoneKg + Math.Sqrt(_fraction) * (s.FullLoadKg - s.DeadZoneKg), s);
    }
}
