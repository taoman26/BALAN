using System.Text.RegularExpressions;
using BALAN.Core.Abstractions;
using BALAN.Device.Transport;
using BALAN.Device.Linux;

namespace BALAN.Device.Linux;

/// <summary>
/// Linux で Balance Board に自動接続する。
/// <list type="number">
/// <item>bluetoothctl で探索し、近くにある Balance Board(赤い SYNC ボタンで探索可能)の Bluetooth アドレスを得る。</item>
/// <item>BlueZ のペアリング(PIN 認証)を使わず、L2CAP で直接接続する。</item>
/// <item>確立した接続を <see cref="LinuxBoardLink"/> 経由で BalanceBoardService へ渡す。</item>
/// </list>
/// </summary>
public sealed partial class LinuxBoardPairing : IBoardPairing
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(6);

    private readonly IAppLog _log;
    private readonly LinuxBoardLink _link;
    private readonly IBluetoothCtl _ctl;
    private readonly IL2capConnector _connector;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _unavailableReported;

    public LinuxBoardPairing(IAppLog log, LinuxBoardLink link)
        : this(log, link, new BluetoothCtl(), new L2capConnector())
    {
    }

    internal LinuxBoardPairing(IAppLog log, LinuxBoardLink link, IBluetoothCtl ctl, IL2capConnector connector)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _ctl = ctl ?? throw new ArgumentNullException(nameof(ctl));
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
    }

    public async Task<PairingResult> TryPairAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 前回の受け渡し残り(使われなかった接続)は破棄する。
            _link.Clear();

            string scan;
            string known;
            try
            {
                await _ctl.RunAsync("power on", TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                scan = await _ctl.RunAsync($"--timeout {(int)ScanDuration.TotalSeconds} scan on", ScanDuration + TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(false);
                known = await _ctl.RunAsync("devices", TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException ex)
            {
                if (!_unavailableReported)
                {
                    _unavailableReported = true;
                    _log.Info(ex.Message);
                }

                return PairingResult.NotSupported;
            }

            var candidates = FindBoards(scan, known);
            if (candidates.Count == 0)
            {
                return PairingResult.NoDeviceFound;
            }

            foreach (var address in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _log.Info($"Balance Board found ({address}), connecting...");
                try
                {
                    var connection = await Task.Run(() => _connector.Connect(address), cancellationToken).ConfigureAwait(false);
                    _link.Set(connection);
                    _log.Info("Balance Board connected");
                    return PairingResult.Paired;
                }
                catch (IOException ex)
                {
                    _log.Info($"Connection failed: {ex.Message}");
                }
            }

            return PairingResult.Failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 今回の探索で存在を確認できた(行に現れた)デバイスのうち、名前が Balance Board のものの
    /// アドレスを返す。名前は探索出力の [NEW]/Name:/Alias: 行と `devices` の一覧から得る。
    /// </summary>
    internal static IReadOnlyList<string> FindBoards(string scanOutput, string devicesOutput)
    {
        var seen = new List<string>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in scanOutput.Split('\n'))
        {
            var line = Clean(raw);
            var match = ScanLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var address = match.Groups["mac"].Value.ToUpperInvariant();
            if (!seen.Contains(address))
            {
                seen.Add(address);
            }

            var rest = match.Groups["rest"].Value.Trim();
            if (rest.StartsWith("Name:", StringComparison.Ordinal) || rest.StartsWith("Alias:", StringComparison.Ordinal))
            {
                names[address] = rest[(rest.IndexOf(':') + 1)..].Trim();
            }
            else if (line.StartsWith("[NEW]", StringComparison.Ordinal) && rest.Length > 0 && !MacLikeName().IsMatch(rest))
            {
                names[address] = rest;
            }
        }

        foreach (var raw in devicesOutput.Split('\n'))
        {
            var match = DeviceLine().Match(Clean(raw));
            if (match.Success)
            {
                var address = match.Groups["mac"].Value.ToUpperInvariant();
                if (!names.ContainsKey(address))
                {
                    names[address] = match.Groups["name"].Value.Trim();
                }
            }
        }

        return seen
            .Where(a => names.TryGetValue(a, out var name)
                && name.StartsWith("Nintendo RVL-WBC-01", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string Clean(string line) => AnsiAndControl().Replace(line, string.Empty).Trim();

    [GeneratedRegex(@"^\[(?:NEW|CHG)\]\s+Device\s+(?<mac>(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2})\s*(?<rest>.*)$")]
    private static partial Regex ScanLine();

    [GeneratedRegex(@"^Device\s+(?<mac>(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2})\s+(?<name>.+)$")]
    private static partial Regex DeviceLine();

    /// <summary>名前が未解決のとき BlueZ が名前欄に出す "00-1F-32-AA-BB-CC" 形式。本当の名前ではない。</summary>
    [GeneratedRegex("^([0-9A-Fa-f]{2}-){5}[0-9A-Fa-f]{2}$")]
    private static partial Regex MacLikeName();

    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]|[\x00-\x08\x0B-\x1F]")]
    private static partial Regex AnsiAndControl();
}
