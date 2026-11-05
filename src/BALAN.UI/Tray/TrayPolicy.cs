using Avalonia.Controls;
using BALAN.Core.Domain;

namespace BALAN.UI.Tray;

/// <summary>トレイ常駐時にウィンドウをどう扱うかの判断(画面部品に依存しない純粋なロジック)。</summary>
public static class TrayPolicy
{
    /// <summary>
    /// 閉じる操作を取り消して、代わりに非表示にするか。
    /// ユーザー自身が閉じた場合だけ。メニューの Exit、OS のシャットダウン、アプリの終了処理では閉じさせる。
    /// </summary>
    public static bool ShouldHideInsteadOfClose(WindowCloseReason reason, bool exiting) =>
        !exiting && reason == WindowCloseReason.WindowClosing;

    /// <summary>最小化されたときに非表示にするか。</summary>
    public static bool ShouldHideOnStateChange(WindowState state, bool exiting) =>
        !exiting && state == WindowState.Minimized;

    /// <summary>接続状態を示すトレイアイコン(右下の丸が 赤=切断 / 黄=接続中 / 緑=接続済み)。</summary>
    public static Uri IconFor(ConnectionState state) => state switch
    {
        ConnectionState.Connected => new Uri("avares://BALAN.UI/Assets/icon_tray_connected.png"),
        ConnectionState.Connecting => new Uri("avares://BALAN.UI/Assets/icon_tray_connecting.png"),
        _ => new Uri("avares://BALAN.UI/Assets/icon_tray_disconnected.png"),
    };
}
