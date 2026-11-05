using System.Text.Json;

namespace BALAN.Core.Settings;

/// <summary>設定の JSON ファイル保存。書き込み途中のクラッシュで壊れないよう、一時ファイル経由で置き換える。</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public JsonSettingsStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>%APPDATA%\BALAN\settings.json(Linux: ~/.config/BALAN/settings.json)。</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BALAN", "settings.json");

    public BalanSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new BalanSettings();
            }

            var loaded = JsonSerializer.Deserialize<BalanSettings>(File.ReadAllText(_path), Options);
            return (loaded ?? new BalanSettings()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 壊れた設定で起動不能にならないよう、既定値で続行する。
            return new BalanSettings();
        }
    }

    public void Save(BalanSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalize(), Options));
        File.Move(temp, _path, overwrite: true);
    }
}
