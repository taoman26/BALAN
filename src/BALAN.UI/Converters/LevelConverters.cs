using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BALAN.Core.Domain;

namespace BALAN.UI.Converters;

/// <summary>巡航段階 → 表示色。</summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    public static readonly LevelToBrushConverter Instance = new();

    private static readonly IBrush Stopped = new SolidColorBrush(Color.Parse("#8B90A3"));
    private static readonly IBrush Slow = new SolidColorBrush(Color.Parse("#56B6C2"));
    private static readonly IBrush Medium = new SolidColorBrush(Color.Parse("#F2C94C"));
    private static readonly IBrush Fast = new SolidColorBrush(Color.Parse("#F2994A"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        CruiseLevel.Slow => Slow,
        CruiseLevel.Medium => Medium,
        CruiseLevel.Fast => Fast,
        _ => Stopped,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>接続状態 → 表示色(赤/黄/緑)。</summary>
public sealed class ConnectionToBrushConverter : IValueConverter
{
    public static readonly ConnectionToBrushConverter Instance = new();

    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#EB5757"));
    private static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#F2C94C"));
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#3ECF6E"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ConnectionState.Connected => Green,
        ConnectionState.Connecting => Yellow,
        _ => Red,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
