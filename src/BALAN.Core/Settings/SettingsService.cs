namespace BALAN.Core.Settings;

/// <summary>現在の設定を保持し、変更を保存する。エンジンは毎周期 <see cref="Current"/> を読むため、変更は即座に反映される。</summary>
public sealed class SettingsService
{
    private readonly ISettingsStore _store;
    private volatile BalanSettings _current;

    public SettingsService(ISettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = store.Load().Normalize();
    }

    public BalanSettings Current => _current;

    public event EventHandler<BalanSettings>? Changed;

    /// <summary>設定を更新する(保存は <see cref="Save"/>)。範囲外の値は丸められる。</summary>
    public void Update(Func<BalanSettings, BalanSettings> change)
    {
        var next = change(_current).Normalize();
        _current = next;
        try
        {
            Changed?.Invoke(this, next);
        }
        catch
        {
            // 購読者の不具合で設定の更新(と巡航)を止めない。
        }
    }

    public void Save() => _store.Save(_current);
}
