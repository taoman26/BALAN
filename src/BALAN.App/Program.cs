using Avalonia;
using Avalonia.ReactiveUI;
using BALAN.Core.Abstractions;
using BALAN.Core.Engine;
using BALAN.Core.Settings;
using BALAN.Device;
using BALAN.Device.Transport;
using BALAN.Device.Linux;
using BALAN.Device.Windows;
using BALAN.Input;
using BALAN.UI;
using BALAN.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BALAN.App;

/// <summary>コンポジションルート。各層(Core / Device / Input / UI)をここでだけ結びつける。</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        CrashLog.Install();
        var simulate = args.Contains("--simulate");
        var options = new AppOptions(
            HideToTray: !args.Contains("--no-tray"),
            StartHidden: args.Contains("--minimized"),
            AutoConnect: !args.Contains("--no-autoconnect"));

        var provider = BuildServices(simulate).BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        try
        {
            var runner = provider.GetRequiredService<EngineRunner>();
            _ = provider.GetRequiredService<BoardEngineBridge>(); // デバイス→エンジンの配線
            runner.Start();

            return BuildAvaloniaApp(() => provider.GetRequiredService<MainViewModel>(), options, provider.GetRequiredService<IAppLog>()).StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public static AppBuilder BuildAvaloniaApp(Func<MainViewModel> viewModelFactory, AppOptions options, IAppLog? log = null) =>
        AppBuilder.Configure(() => new UI.App(viewModelFactory, options, log))
            .UsePlatformDetect()
            .WithInterFont()
            .UseReactiveUI()
            .AfterSetup(_ => CrashLog.InstallUiHandler())
            .LogToTrace();

    internal static IServiceCollection BuildServices(bool simulate)
    {
        var services = new ServiceCollection();

        // Core
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<MessageLog>();
        services.AddSingleton<IAppLog>(p => p.GetRequiredService<MessageLog>());
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(JsonSettingsStore.DefaultPath));
        services.AddSingleton<SettingsService>();
        services.AddSingleton<BalanEngine>();
        services.AddSingleton<EngineRunner>();

        // Input(OS へのスクロール送出)
        services.AddSingleton<IScrollSender>(_ => ScrollSenderFactory.Create());

        // Device(Balance Board。--simulate なら模擬ボード)
        if (simulate)
        {
            services.AddSingleton<SimulatedBalanceBoard>();
            services.AddSingleton<IBalanceBoard>(p => p.GetRequiredService<SimulatedBalanceBoard>());
        }
        else
        {
            if (OperatingSystem.IsLinux())
            {
                // Linux: BlueZ(bluetoothctl)で探索し、L2CAP で直接接続する。確立した接続を LinuxBoardTransport が優先して使う。
                services.AddSingleton<LinuxBoardLink>();
                services.AddSingleton<IBoardTransport>(p => new LinuxBoardTransport(new HidSharpBoardTransport(), p.GetRequiredService<LinuxBoardLink>()));
                services.AddSingleton<IBoardPairing>(p => new LinuxBoardPairing(p.GetRequiredService<IAppLog>(), p.GetRequiredService<LinuxBoardLink>()));
            }
            else
            {
                services.AddSingleton<IBoardTransport, HidSharpBoardTransport>();
                services.AddSingleton<IBoardPairing>(p => OperatingSystem.IsWindows()
                    ? new WindowsBoardPairing(p.GetRequiredService<IAppLog>())
                    : new NoPairing());
            }

            services.AddSingleton(p => new BalanceBoardService(
                p.GetRequiredService<IBoardTransport>(),
                BalanceBoardOptions.Default,
                p.GetRequiredService<IBoardPairing>()));
            services.AddSingleton<IBalanceBoard>(p => p.GetRequiredService<BalanceBoardService>());
        }

        services.AddSingleton<BoardEngineBridge>();

        // UI
        services.AddSingleton<MainViewModel>();
        return services;
    }

    /// <summary>Windows / Linux 以外では OS 側でペアリングしてもらう。</summary>
    private sealed class NoPairing : IBoardPairing
    {
        public Task<PairingResult> TryPairAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PairingResult.NotSupported);
    }
}
