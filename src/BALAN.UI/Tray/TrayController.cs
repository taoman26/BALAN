using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using BALAN.UI.ViewModels;

namespace BALAN.UI.Tray;

/// <summary>
/// 常駐の要。ウィンドウの「閉じる」「最小化」を「非表示」に置き換え、トレイ(<see cref="ITrayBackend"/>)と ViewModel をつなぐ。
/// 終了はトレイメニューの Exit から。アイコンの右下の丸が接続状態(赤/黄/緑)を表す。
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly Window _window;
    private readonly MainViewModel _viewModel;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly ITrayBackend _backend;
    private readonly IDisposable _stateSubscription;
    private bool _exiting;
    private bool _disposed;

    public TrayController(
        IClassicDesktopStyleApplicationLifetime lifetime,
        Window window,
        MainViewModel viewModel,
        ITrayBackend backend)
    {
        _lifetime = lifetime;
        _window = window;
        _viewModel = viewModel;
        _backend = backend;

        _backend.SetState(viewModel.State);
        _backend.SetToolTip(viewModel.TrayToolTip);

        _window.Closing += OnClosing;
        _stateSubscription = _window.GetObservable(Window.WindowStateProperty).Subscribe(new StateObserver(OnStateChanged));

        _viewModel.PropertyChanged += OnViewModelChanged;
        _viewModel.ShowWindowRequested += OnShow;
        _viewModel.MinimizeRequested += OnMinimize;
        _viewModel.ExitRequested += OnExit;
        _lifetime.Exit += OnLifetimeExit;
    }

    /// <summary>使っているトレイ方式の名前(ログ用)。</summary>
    public string BackendName => _backend.Name;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.ShowWindowRequested -= OnShow;
        _viewModel.MinimizeRequested -= OnMinimize;
        _viewModel.ExitRequested -= OnExit;
        _lifetime.Exit -= OnLifetimeExit;
        _window.Closing -= OnClosing;
        _stateSubscription.Dispose();
        _backend.Dispose();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.State))
        {
            _backend.SetState(_viewModel.State);
        }

        if (e.PropertyName is nameof(MainViewModel.TrayToolTip))
        {
            _backend.SetToolTip(_viewModel.TrayToolTip);
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (TrayPolicy.ShouldHideInsteadOfClose(e.CloseReason, _exiting))
        {
            e.Cancel = true;
            _window.Hide();
        }
    }

    private void OnStateChanged(WindowState state)
    {
        if (!TrayPolicy.ShouldHideOnStateChange(state, _exiting))
        {
            return;
        }

        // 状態変更の通知の最中にウィンドウを操作しないよう、少し後で行う。
        // 非表示にする前に通常状態へ戻しておき、再表示時に最小化のままにならないようにする。
        Dispatcher.UIThread.Post(() =>
        {
            _window.WindowState = WindowState.Normal;
            _window.Hide();
        });
    }

    private void OnShow(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    });

    private void OnMinimize(object? sender, EventArgs e) => Dispatcher.UIThread.Post(_window.Hide);

    private void OnExit(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        _exiting = true;
        _lifetime.Shutdown();
    });

    private void OnLifetimeExit(object? sender, ControlledApplicationLifetimeExitEventArgs e) => Dispose();

    private sealed class StateObserver : IObserver<WindowState>
    {
        private readonly Action<WindowState> _onNext;

        public StateObserver(Action<WindowState> onNext) => _onNext = onNext;

        public void OnNext(WindowState value) => _onNext(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
