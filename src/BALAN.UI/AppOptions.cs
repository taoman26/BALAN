namespace BALAN.UI;

/// <summary>起動オプション。</summary>
/// <param name="HideToTray">閉じる/最小化でトレイへ隠す(常駐)。</param>
/// <param name="StartHidden">起動時にウィンドウを出さずトレイだけ。</param>
/// <param name="AutoConnect">起動時に自動で接続を試みる。</param>
public sealed record AppOptions(bool HideToTray = true, bool StartHidden = false, bool AutoConnect = true);
