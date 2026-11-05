namespace BALAN.Core.Domain;

/// <summary>Balance Board の 1 回分の荷重(各センサー、kg)。</summary>
public readonly record struct SensorFrame(double TopLeft, double TopRight, double BottomLeft, double BottomRight)
{
    public double Total => TopLeft + TopRight + BottomLeft + BottomRight;

    public double Left => TopLeft + BottomLeft;

    public double Right => TopRight + BottomRight;
}
