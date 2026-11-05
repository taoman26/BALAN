using BALAN.Core.Domain;

namespace BALAN.UI.Tray;

/// <summary>トレイアイコンの実体。OS・デスクトップ環境ごとに表示方式が異なるため、差し替えられるようにしてある。</summary>
public interface ITrayBackend : IDisposable
{
    /// <summary>方式の名前(ログ用)。</summary>
    string Name { get; }

    /// <summary>接続状態に合わせてアイコンの色を変える。</summary>
    void SetState(ConnectionState state);

    /// <summary>ツールチップの文字列。対応しない方式では何もしない。</summary>
    void SetToolTip(string text);
}
