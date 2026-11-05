using System.Diagnostics;
using Avalonia;
using BALAN.Core.Abstractions;
using BALAN.UI.Tray.X11;
using BALAN.UI.ViewModels;

namespace BALAN.UI.Tray;

/// <summary>
/// 実行環境で使えるトレイ方式を選ぶ。
/// Windows: Avalonia 標準。
/// Linux: XEmbed(Xfce・Enlightenment など従来型)→ StatusNotifierItem(KDE・GNOME の拡張など)の順に試し、
/// どちらも使えなければ null(その場合は、ウィンドウを隠すと戻れなくなるので、トレイ常駐は行わない)。
/// 環境変数 <c>BALAN_TRAY</c> で方式を指定できる(<c>xembed</c> / <c>sni</c> / <c>none</c>)。
/// </summary>
public static class TrayBackendFactory
{
    public static ITrayBackend? Create(Application application, MainViewModel viewModel, IAppLog log)
    {
        try
        {
            if (!OperatingSystem.IsLinux())
            {
                return new AvaloniaTrayBackend(application, viewModel);
            }

            var forced = Environment.GetEnvironmentVariable("BALAN_TRAY")?.Trim().ToLowerInvariant();
            if (forced == "none")
            {
                log.Info("System tray disabled by BALAN_TRAY=none");
                return null;
            }

            if (forced is null or "" or "xembed")
            {
                var xembed = X11TrayBackend.TryCreate(viewModel);
                if (xembed is not null)
                {
                    return xembed;
                }
            }

            if (forced is null or "" or "sni")
            {
                if (forced == "sni" || StatusNotifierWatcherExists())
                {
                    return new AvaloniaTrayBackend(application, viewModel);
                }
            }

            log.Info("No system tray found (neither XEmbed nor StatusNotifier). The close button will exit the app.");
            return null;
        }
        catch (Exception ex)
        {
            log.Info($"System tray unavailable: {ex.Message}");
            return null;
        }
    }

    /// <summary>DBus のセッションバスに StatusNotifierWatcher(SNI の受け口)があるか。調べられなければ、あるものとみなす。</summary>
    private static bool StatusNotifierWatcherExists()
    {
        try
        {
            var info = new ProcessStartInfo("dbus-send",
                "--session --print-reply --dest=org.freedesktop.DBus /org/freedesktop/DBus " +
                "org.freedesktop.DBus.NameHasOwner string:org.kde.StatusNotifierWatcher")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(info);
            if (process is null)
            {
                return true;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return true;
            }

            return process.ExitCode != 0 || output.Contains("boolean true", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return true; // dbus-send が無いなど。判定できないので従来どおり SNI を試す。
        }
    }
}
