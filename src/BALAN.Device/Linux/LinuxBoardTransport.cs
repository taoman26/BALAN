using BALAN.Device.Transport;

namespace BALAN.Device.Linux;

/// <summary>
/// 自動接続(<see cref="LinuxBoardPairing"/>)で確立した L2CAP 接続を優先し、無ければ従来どおり hidraw(HidSharp)から探す。
/// </summary>
public sealed class LinuxBoardTransport : IBoardTransport
{
    private readonly IBoardTransport _inner;
    private readonly LinuxBoardLink _link;

    public LinuxBoardTransport(IBoardTransport inner, LinuxBoardLink link)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _link = link ?? throw new ArgumentNullException(nameof(link));
    }

    public IEnumerable<IBoardConnection> OpenCandidates()
    {
        if (_link.Take() is { } linked)
        {
            yield return linked;
        }

        foreach (var candidate in _inner.OpenCandidates())
        {
            yield return candidate;
        }
    }
}
