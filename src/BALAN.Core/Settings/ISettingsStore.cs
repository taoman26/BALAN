namespace BALAN.Core.Settings;

public interface ISettingsStore
{
    /// <summary>保存済みの設定を読む。無い/壊れている場合は既定値。</summary>
    BalanSettings Load();

    void Save(BalanSettings settings);
}
