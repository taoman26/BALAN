namespace BALAN.Core.Abstractions;

/// <summary>時刻の抽象。テストで時間を進められるようにする。</summary>
public interface IClock
{
    TimeSpan Now { get; }
}

public sealed class SystemClock : IClock
{
    private static readonly long Origin = System.Diagnostics.Stopwatch.GetTimestamp();

    public TimeSpan Now => System.Diagnostics.Stopwatch.GetElapsedTime(Origin);
}
