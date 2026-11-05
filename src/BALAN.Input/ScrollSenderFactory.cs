using BALAN.Core.Abstractions;
using BALAN.Input.Linux;

namespace BALAN.Input;

public static class ScrollSenderFactory
{
    /// <summary>実行中の OS に合わせた送出実装。Windows は SendInput、Linux は uinput。それ以外は画面表示のみ(Null 実装)。</summary>
    public static IScrollSender Create() =>
        OperatingSystem.IsWindows() ? new WindowsScrollSender()
        : OperatingSystem.IsLinux() ? new LinuxScrollSender()
        : new NullScrollSender();
}
