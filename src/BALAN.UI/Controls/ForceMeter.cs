using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BALAN.UI.Controls;

/// <summary>
/// 荷重(kg)を横バーで示す。しきい値(反応が始まる荷重)に目印を付け、超えている間は色を付ける。
/// 足の荷重の「あとどれだけで効くか」を見るためのもの。
/// </summary>
public sealed class ForceMeter : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ForceMeter, double>(nameof(Value));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<ForceMeter, double>(nameof(Maximum), 8);
    public static readonly StyledProperty<double> ThresholdProperty = AvaloniaProperty.Register<ForceMeter, double>(nameof(Threshold));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<ForceMeter, IBrush?>(nameof(Fill));

    private static readonly IBrush Field = new SolidColorBrush(Color.Parse("#17181D"));
    private static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#5A5E70"));
    private static readonly IPen Edge = new Pen(new SolidColorBrush(Color.Parse("#3A3D4A")), 1);
    private static readonly IPen Mark = new Pen(new SolidColorBrush(Color.Parse("#E6E8EF")), 2);

    static ForceMeter()
    {
        AffectsRender<ForceMeter>(ValueProperty, MaximumProperty, ThresholdProperty, FillProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// <summary>反応が始まる荷重(kg)。</summary>
    public double Threshold { get => GetValue(ThresholdProperty); set => SetValue(ThresholdProperty, value); }

    /// <summary>しきい値を超えたときの色。</summary>
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, 20);

    public override void Render(DrawingContext context)
    {
        var max = Math.Max(Maximum, 0.1);
        var w = Bounds.Width;
        var cy = Bounds.Height / 2;
        context.DrawRectangle(Field, Edge, new Rect(0, cy - 7, w, 14), 7, 7);

        var value = Math.Clamp(Value, 0, max);
        if (value > 0.02)
        {
            var active = Value >= Threshold;
            var fillWidth = Math.Max(w * value / max, 6);
            context.DrawRectangle(active ? Fill ?? Idle : Idle, null, new Rect(1, cy - 6, Math.Min(fillWidth, w - 2), 12), 6, 6);
        }

        var x = w * Math.Clamp(Threshold / max, 0, 1);
        context.DrawLine(Mark, new Point(x, cy - 10), new Point(x, cy + 10));
    }
}
