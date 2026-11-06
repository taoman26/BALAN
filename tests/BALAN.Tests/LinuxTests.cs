using BALAN.Device.Linux;
using BALAN.Input.Linux;
using Xunit;

namespace BALAN.Tests;

public class LinuxScrollSenderTests
{
    private sealed class FakeSink : IInputEventSink
    {
        public List<(ushort Type, ushort Code, int Value)> Events { get; } = new();
        public bool Disposed { get; private set; }
        public void Write(ushort type, ushort code, int value) => Events.Add((type, code, value));
        public void Dispose() => Disposed = true;
    }

    private const ushort Rel = UInputMouse.EvRel;
    private const ushort HiRes = UInputMouse.RelWheelHiRes;
    private const ushort Wheel = UInputMouse.RelWheel;

    [Fact]
    public void HorizontalUsesHWheelCodesWithOwnRemainder()
    {
        var sink = new FakeSink();
        var sender = new LinuxScrollSender(() => sink);
        sender.SendHorizontal(90);
        sender.SendVertical(60); // 垂直の端数と混ざらない
        sender.SendHorizontal(90);
        var codes = sink.Events.Where(e => e.Type == Rel).Select(e => (e.Code, e.Value)).ToArray();
        Assert.Equal(new (ushort, int)[]
        {
            (UInputMouse.RelHWheelHiRes, 90),
            (HiRes, 60),
            (UInputMouse.RelHWheelHiRes, 90),
            (UInputMouse.RelHWheel, 1),
        }, codes);
    }

    [Fact]
    public void SendsHiResEveryTimeAndSynReport()
    {
        var sink = new FakeSink();
        var sender = new LinuxScrollSender(() => sink);
        sender.SendVertical(-30);
        Assert.Equal(new[] { (Rel, HiRes, -30), (UInputMouse.EvSyn, (ushort)0, 0) }, sink.Events.Select(e => (e.Type, e.Code, e.Value)).ToArray());
    }

    [Fact]
    public void LowResWheelIsEmittedOncePerFullNotch()
    {
        var sink = new FakeSink();
        var sender = new LinuxScrollSender(() => sink);
        for (var i = 0; i < 8; i++) sender.SendVertical(-30); // -240 = 2 ノッチ(下)
        var lowRes = sink.Events.Where(e => e.Code == Wheel && e.Type == Rel).ToList();
        Assert.Equal(new[] { -1, -1 }, lowRes.Select(e => e.Value).ToArray());
        Assert.Equal(-240, sink.Events.Where(e => e.Code == HiRes).Sum(e => e.Value));
    }

    [Fact]
    public void UpwardScrollIsPositive()
    {
        var sink = new FakeSink();
        var sender = new LinuxScrollSender(() => sink);
        sender.SendVertical(120);
        Assert.Contains(sink.Events, e => e.Code == Wheel && e.Value == 1);
    }

    [Fact]
    public void DeviceIsCreatedLazilyOnceAndDisposed()
    {
        var created = 0;
        var sink = new FakeSink();
        var sender = new LinuxScrollSender(() => { created++; return sink; });
        Assert.Equal(0, created);
        sender.SendVertical(30);
        sender.SendVertical(30);
        Assert.Equal(1, created);
        sender.Dispose();
        Assert.True(sink.Disposed);
        Assert.Throws<ObjectDisposedException>(() => sender.SendVertical(30));
    }

    [Fact]
    public void ZeroDeltaDoesNothingAndDoesNotCreateDevice()
    {
        var created = 0;
        new LinuxScrollSender(() => { created++; return new FakeSink(); }).SendVertical(0);
        Assert.Equal(0, created);
    }

    [Fact]
    public void FactoryFailurePropagatesWithMessage()
    {
        var sender = new LinuxScrollSender(() => throw new IOException("Cannot open /dev/uinput"));
        var ex = Assert.Throws<IOException>(() => sender.SendVertical(30));
        Assert.Contains("uinput", ex.Message);
    }

    [Fact]
    public void InputEventLayout()
    {
        var e = UInputMouse.BuildInputEvent(Rel, HiRes, -30);
        var off = 2 * IntPtr.Size;
        Assert.Equal(off + 8, e.Length);
        Assert.Equal(Rel, BitConverter.ToUInt16(e, off));
        Assert.Equal(HiRes, BitConverter.ToUInt16(e, off + 2));
        Assert.Equal(-30, BitConverter.ToInt32(e, off + 4));
        Assert.Equal(1116, UInputMouse.BuildUserDev("x").Length);
    }
}

public class LinuxPairingTests
{
    [Fact]
    public void FindsBalanceBoardButNotWiiRemote()
    {
        var scan = "[NEW] Device 00:1F:32:AA:BB:CC Nintendo RVL-WBC-01\n[NEW] Device 00:1F:32:11:22:33 Nintendo RVL-CNT-01\n[NEW] Device 11:22:33:44:55:66 Keyboard\n";
        var found = LinuxBoardPairing.FindBoards(scan, string.Empty);
        Assert.Equal(new[] { "00:1F:32:AA:BB:CC" }, found.ToArray());
    }

    [Fact]
    public void UsesKnownDevicesListForNameAndIgnoresAnsi()
    {
        var scan = "\u001b[0;92m[NEW]\u001b[0m Device 00:1F:32:AA:BB:CC 00-1F-32-AA-BB-CC\n";
        var devices = "Device 00:1F:32:AA:BB:CC Nintendo RVL-WBC-01\n";
        Assert.Single(LinuxBoardPairing.FindBoards(scan, devices));
    }

    [Fact]
    public void L2capSockAddrLayout()
    {
        var bd = L2capConnector.ParseAddress("00:1F:32:AA:BB:CC");
        Assert.Equal(new byte[] { 0xCC, 0xBB, 0xAA, 0x32, 0x1F, 0x00 }, bd);
        var addr = L2capConnector.BuildSockAddr(bd, 0x13);
        Assert.Equal(14, addr.Length);
        Assert.Equal(31, addr[0]);
        Assert.Equal(0x13, addr[2]);
        Assert.Equal(bd, addr.Skip(4).Take(6).ToArray());
        Assert.Throws<ArgumentException>(() => L2capConnector.ParseAddress("nope"));
    }
}
