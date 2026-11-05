using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BALAN.Core.Abstractions;
using BALAN.UI.Tray;
using BALAN.UI.ViewModels;
using BALAN.UI.Views;

namespace BALAN.UI;

/// <summary>起動・終了のつなぎ込みのみ。画面のロジックは ViewModel にある。</summary>
public partial class App : Application
{
    private readonly Func<MainViewModel>? _viewModelFactory;
    private readonly AppOptions _options;
    private MainViewModel? _viewModel;
    private readonly IAppLog _log = new NullAppLog();
    private TrayController? _tray;

    /// <summary>XAML デザイナー用。実行時は使わない。</summary>
    public App()
    {
        _options = new AppOptions();
    }

    /// <param name="viewModelFactory">
    /// ViewModel の生成。ReactiveUI のスケジューラ(UI スレッド)が Avalonia の初期化で設定されるより前に作ると、
    /// コマンドの状態変化が別スレッドで通知されて落ちるため、初期化完了後(ここ)で呼ぶ。
    /// </param>
    public App(Func<MainViewModel> viewModelFactory, AppOptions options, IAppLog? log = null)
    {
        _viewModelFactory = viewModelFactory;
        _options = options;
        _log = log ?? new NullAppLog();
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && _viewModelFactory is not null)
        {
            var viewModel = _viewModel = _viewModelFactory();
            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;

            // トレイ方式は環境に合わせて選ぶ。使える方式が無ければ、隠すと戻れなくなるので常駐(非表示化)しない。
            var backend = _options.HideToTray ? TrayBackendFactory.Create(this, viewModel, _log) : null;
            if (backend is not null)
            {
                _log.Info($"System tray: {backend.Name}");
                _tray = new TrayController(desktop, window, viewModel, backend);
                if (_options.StartHidden)
                {
                    // 最初から表示しない(常駐起動)。MainWindow に設定済みでも Show しなければ出ない。
                    window.ShowActivated = false;
                    window.Opened += (_, _) => window.Hide();
                }
            }

            if (_options.AutoConnect)
            {
                window.Opened += async (_, _) => await viewModel.InitializeAsync();
            }

            // 終了時: 設定を保存し、切断してから改めて終了する。
            var shutdownDone = false;
            desktop.ShutdownRequested += async (_, e) =>
            {
                if (shutdownDone)
                {
                    return;
                }

                e.Cancel = true;
                await viewModel.ShutdownAsync();
                shutdownDone = true;
                _tray?.Dispose();
                desktop.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
