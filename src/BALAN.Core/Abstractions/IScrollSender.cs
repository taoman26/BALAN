namespace BALAN.Core.Abstractions;

/// <summary>OS へスクロール(マウスホイール)を送出する。BALAN.Input が実装する。</summary>
public interface IScrollSender
{
    /// <summary>
    /// 垂直スクロールを送る。120 = ホイール 1 ノッチ。正 = 上方向、負 = 下方向(Windows のホイールと同じ)。
    /// </summary>
    void SendVertical(int wheelDelta);

    /// <summary>
    /// 水平スクロール(チルトホイール)を送る。120 = 1 ノッチ。正 = 右方向、負 = 左方向(Windows の水平ホイール、Linux の REL_HWHEEL と同じ)。
    /// </summary>
    void SendHorizontal(int wheelDelta);
}
