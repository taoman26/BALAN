using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BALAN.UI.ViewModels;

namespace BALAN.UI.Tray;

/// <summary>
/// XEmbed 方式のトレイにはメニュー機能が無いため、右クリックで出す簡易メニュー(枠なしの小さなウィンドウ)。
/// 項目: Show BALAN / Connect(Disconnect)/ Scroll Mode(クリックで切替)/ Exit。他の場所を操作すると閉じる。
/// </summary>
public sealed class TrayMenuWindow : Window
{
    public TrayMenuWindow(MainViewModel viewModel)
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = new SolidColorBrush(Color.Parse("#2B2D38"));

        var connect = MenuButton(string.Empty, viewModel.ConnectCommand);
        connect.Bind(ContentProperty, new Avalonia.Data.Binding(nameof(MainViewModel.ConnectButtonText)) { Source = viewModel });

        var mode = MenuButton(string.Empty, viewModel.ToggleScrollModeCommand);
        mode.Bind(ContentProperty, new Avalonia.Data.Binding(nameof(MainViewModel.ScrollModeMenuText)) { Source = viewModel });

        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#4A4D5E")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = new StackPanel
            {
                MinWidth = 240,
                Children =
                {
                    MenuButton("Show BALAN", viewModel.ShowWindowCommand),
                    connect,
                    mode,
                    new Border { Height = 1, Margin = new Thickness(4, 4), Background = new SolidColorBrush(Color.Parse("#4A4D5E")) },
                    MenuButton("Exit", viewModel.ExitCommand),
                },
            },
        };

        Deactivated += (_, _) => Close();
    }

    /// <summary>画面上のカーソル位置の近く(画面内に収まる位置)に表示する。</summary>
    public void ShowAt(PixelPoint cursor)
    {
        Opened += (_, _) =>
        {
            var screen = Screens.ScreenFromPoint(cursor) ?? Screens.Primary;
            var size = PixelSize.FromSize(Bounds.Size, screen?.Scaling ?? 1.0);
            var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
            var x = Math.Min(cursor.X, area.Right - size.Width);
            // トレイが画面の下側にあれば上へ、上側なら下へ開く。
            var y = cursor.Y > area.Y + area.Height / 2 ? cursor.Y - size.Height : cursor.Y;
            Position = new PixelPoint(Math.Max(area.X, x), Math.Max(area.Y, y));
            Activate();
        };
        Position = cursor;
        Show();
    }

    private Button MenuButton(string text, System.Windows.Input.ICommand command)
    {
        var button = new Button
        {
            Content = text,
            Command = command,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            Padding = new Thickness(12, 6),
        };
        button.Click += (_, _) => Close();
        return button;
    }
}
