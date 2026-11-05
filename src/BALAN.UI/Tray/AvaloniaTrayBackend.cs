using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform;
using Avalonia.Threading;
using BALAN.Core.Domain;
using BALAN.UI.ViewModels;

namespace BALAN.UI.Tray;

/// <summary>
/// Avalonia 標準のトレイ。Windows ではそのまま、Linux では DBus の StatusNotifierItem 方式
/// (KDE、GNOME の AppIndicator 拡張、SNI 対応パネルなど)で表示される。
/// </summary>
public sealed class AvaloniaTrayBackend : ITrayBackend
{
    private readonly TrayIcon _icon;
    private readonly Dictionary<Uri, WindowIcon> _icons = new();

    public AvaloniaTrayBackend(Application application, MainViewModel viewModel)
    {
        var connect = new NativeMenuItem { Command = viewModel.ConnectCommand };
        connect.Bind(NativeMenuItem.HeaderProperty, new Binding(nameof(MainViewModel.ConnectButtonText)) { Source = viewModel });

        _icon = new TrayIcon
        {
            Menu = new NativeMenu
            {
                new NativeMenuItem { Header = "Show BALAN", Command = viewModel.ShowWindowCommand },
                connect,
                new NativeMenuItemSeparator(),
                new NativeMenuItem { Header = "Exit", Command = viewModel.ExitCommand },
            },
            Command = viewModel.ShowWindowCommand,
            Icon = IconFor(viewModel.State),
            ToolTipText = viewModel.TrayToolTip,
            IsVisible = true,
        };
        TrayIcon.SetIcons(application, new TrayIcons { _icon });
    }

    public string Name => "StatusNotifierItem";

    public void SetState(ConnectionState state) =>
        Dispatcher.UIThread.Post(() => _icon.Icon = IconFor(state));

    public void SetToolTip(string text) =>
        Dispatcher.UIThread.Post(() => _icon.ToolTipText = text);

    public void Dispose()
    {
        // 終了後にアイコンだけがトレイに残らないよう、確実に消す。
        _icon.IsVisible = false;
        _icon.Dispose();
    }

    private WindowIcon IconFor(ConnectionState state)
    {
        var uri = TrayPolicy.IconFor(state);
        if (!_icons.TryGetValue(uri, out var icon))
        {
            icon = new WindowIcon(AssetLoader.Open(uri));
            _icons[uri] = icon;
        }

        return icon;
    }
}
