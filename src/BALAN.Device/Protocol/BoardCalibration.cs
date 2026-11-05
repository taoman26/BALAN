using BALAN.Core.Domain;

namespace BALAN.Device.Protocol;

/// <summary>
/// ボード内蔵の較正値(0 kg / 17 kg / 34 kg の各センサーの生値)。生値を kg に直す。
/// 実機が持つ値なので、ユーザー側の較正は不要(中立点の補正は BALAN の Calibrate が別に行う)。
/// </summary>
public sealed class BoardCalibration
{
    private readonly ushort[][] _points; // [センサー 0=TL,1=TR,2=BL,3=BR][0kg,17kg,34kg]

    private BoardCalibration(ushort[][] points) => _points = points;

    /// <summary>
    /// 0xA40024 からの 24 バイト: 0 kg(TR, BR, TL, BL)、17 kg(同)、34 kg(同)。各 16 ビット ビッグエンディアン。
    /// 値が単調増加でない(読み出し失敗・別デバイス)場合は null。
    /// </summary>
    public static BoardCalibration? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < BoardProtocol.CalibrationLength)
        {
            return null;
        }

        // 配置順(TR,BR,TL,BL)→ 内部順(TL,TR,BL,BR)。
        int[] fileIndexOfSensor = { 2, 0, 3, 1 };
        var points = new ushort[4][];
        for (var s = 0; s < 4; s++)
        {
            points[s] = new ushort[3];
            for (var level = 0; level < 3; level++)
            {
                var offset = (level * 4 + fileIndexOfSensor[s]) * 2;
                points[s][level] = (ushort)((data[offset] << 8) | data[offset + 1]);
            }

            if (!(points[s][0] < points[s][1] && points[s][1] < points[s][2]))
            {
                return null;
            }
        }

        return new BoardCalibration(points);
    }

    public SensorFrame ToKilograms(RawSensors raw) => new(
        Convert(0, raw.TopLeft),
        Convert(1, raw.TopRight),
        Convert(2, raw.BottomLeft),
        Convert(3, raw.BottomRight));

    private double Convert(int sensor, ushort value)
    {
        var p = _points[sensor];
        // 0〜17 kg と 17〜34 kg を別々に線形補間(34 kg を超えても外挿する)。
        var kg = value < p[1]
            ? 17.0 * (value - p[0]) / (p[1] - p[0])
            : 17.0 + 17.0 * (value - p[1]) / (p[2] - p[1]);
        return Math.Max(kg, 0);
    }
}
