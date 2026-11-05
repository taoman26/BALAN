using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace BALAN.Device.Linux;

/// <summary>BlueZ の bluetoothctl を実行する。</summary>
internal interface IBluetoothCtl
{
    /// <summary>
    /// bluetoothctl を引数付きで実行し、標準出力を返す。<paramref name="timeout"/> を過ぎたら強制終了して、そこまでの出力を返す。
    /// </summary>
    /// <exception cref="FileNotFoundException">bluetoothctl が無い(bluez 未インストール)。</exception>
    Task<string> RunAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed class BluetoothCtl : IBluetoothCtl
{
    public async Task<string> RunAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("bluetoothctl", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new FileNotFoundException("bluetoothctl could not be started.");
        }
        catch (Win32Exception ex)
        {
            throw new FileNotFoundException("bluetoothctl was not found (install the 'bluez' package).", ex);
        }

        using (process)
        {
            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (output)
                    {
                        output.AppendLine(e.Data);
                    }
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.StandardInput.Close();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                cancellationToken.ThrowIfCancellationRequested();
            }

            lock (output)
            {
                return output.ToString();
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // すでに終了している。
        }
    }
}
