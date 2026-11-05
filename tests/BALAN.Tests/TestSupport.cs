using BALAN.Core.Abstractions;
using BALAN.Core.Domain;
using BALAN.Core.Settings;

namespace BALAN.Tests;

internal sealed class FakeClock : IClock
{
    public TimeSpan Now { get; set; }

    public void Advance(double seconds) => Now += TimeSpan.FromSeconds(seconds);
}

internal sealed class RecordingSender : IScrollSender
{
    public List<int> Sent { get; } = new();

    public int Total => Sent.Sum();

    public void SendVertical(int wheelDelta) => Sent.Add(wheelDelta);
}

internal sealed class MemoryStore : ISettingsStore
{
    public BalanSettings Value { get; set; } = new();

    public int Saves { get; private set; }

    public BalanSettings Load() => Value;

    public void Save(BalanSettings settings)
    {
        Value = settings;
        Saves++;
    }
}

