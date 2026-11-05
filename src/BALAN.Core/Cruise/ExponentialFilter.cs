namespace BALAN.Core.Cruise;

/// <summary>指数移動平均。filtered = old * (1 - alpha) + new * alpha(既定 0.8 / 0.2)。</summary>
public sealed class ExponentialFilter
{
    private readonly double _alpha;
    private double _value;
    private bool _primed;

    public ExponentialFilter(double alpha = 0.2)
    {
        if (alpha is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), "alpha must be in (0, 1].");
        }

        _alpha = alpha;
    }

    public double Value => _value;

    /// <summary>最初の値はそのまま採用する(0 から立ち上がって誤って動くのを避ける)。</summary>
    public double Add(double sample)
    {
        if (!_primed)
        {
            _value = sample;
            _primed = true;
        }
        else
        {
            _value = _value * (1 - _alpha) + sample * _alpha;
        }

        return _value;
    }

    public void Reset()
    {
        _primed = false;
        _value = 0;
    }
}
